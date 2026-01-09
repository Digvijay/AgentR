import logging
import asyncio
import json
import uuid
from typing import Optional, Callable, Dict, Any, Awaitable
from signalrcore.hub_connection_builder import HubConnectionBuilder
from .frames import (
    AgentFrame, ToolResponseFrame, RoomInfo
)

# Type alias for async callbacks
AsyncCallback = Callable[..., Awaitable[None]]

class AgentRClient:
    def __init__(self, hub_url: str, agent_id: str, access_token: str):
        self.agent_id = agent_id
        try:
            self.loop = asyncio.get_running_loop()
        except RuntimeError:
            self.loop = asyncio.new_event_loop()
            asyncio.set_event_loop(self.loop)
            
        self._pending_requests: Dict[str, asyncio.Future] = {}
        
        # Events (Callbacks must be async or bridged)
        self.on_turn_granted: Optional[AsyncCallback] = None 
        self.on_assignment: Optional[Callable[[str], Awaitable[None]]] = None
        self.on_discovery: Optional[Callable[[list[RoomInfo]], Awaitable[None]]] = None
        self.on_floor_open: Optional[AsyncCallback] = None
        
        self.current_room: Optional[str] = None
        self._connected_event = asyncio.Event()

        # Configure SignalR (JSON Protocol by default for max compatibility)
        self.hub_connection = HubConnectionBuilder()\
            .with_url(hub_url, options={
                "access_token_factory": lambda: access_token
            })\
            .with_automatic_reconnect({
                "type": "raw",
                "keep_alive_interval": 10,
                "reconnect_interval": 5,
                "max_attempts": 5
            })\
            .build()

        self.hub_connection.on("ReceiveSignal", self._handle_signal_wrapper)
        self.hub_connection.on_open(lambda: self.loop.call_soon_threadsafe(self._connected_event.set))
        self.hub_connection.on_close(lambda: logging.info("❌ Disconnected"))

    async def connect(self):
        """Starts the connection and waits for the handshake."""
        self.hub_connection.start()
        await self._connected_event.wait()
        logging.info("✅ Connected to AgentR Hub (Async)!")

    async def join_room(self, room_id: str, tags: list[str] = None):
        logging.info(f"🚪 Joining room: {room_id}...")
        self.current_room = room_id
        self.hub_connection.send("JoinRoom", [room_id, tags or []])

    async def leave_room(self, room_id: str):
        if self.current_room == room_id:
            self.current_room = None
        self.hub_connection.send("LeaveRoom", [room_id])

    async def request_turn(self, room_id: str = None):
        target = room_id or self.current_room
        logging.info(f"✋ Requesting turn in {target}...")
        self.hub_connection.send("RequestTurn", [target])

    async def request_agent(self, agent_type: str):
        logging.info(f"⏳ Requesting agent type: {agent_type}...")
        self.hub_connection.send("RequestAgent", [agent_type])

    async def invoke_tool(self, room_id: str, server: str, tool: str, args: dict, timeout: float = 30.0) -> str:
        """
        Invokes an MCP tool and waits for the result (Architect's Pattern).
        """
        request_id = str(uuid.uuid4())
        # SAFETY: Always get the loop intrinsic to this coroutine execution
        loop = asyncio.get_running_loop()
        future = loop.create_future()
        
        self._pending_requests[request_id] = future
        
        logging.info(f"🔌 Invoking tool {server}/{tool} [{request_id}]...")
        
        self.hub_connection.send("InvokeTool", [{
            "SenderId": self.agent_id,
            "RoomId": room_id,
            "RequestId": request_id,
            "ServerName": server,
            "ToolName": tool,
            "ArgumentsJson": json.dumps(args)
        }])

        try:
            # Wait for response with timeout
            result_json = await asyncio.wait_for(future, timeout)
            return result_json
        except asyncio.TimeoutError:
            del self._pending_requests[request_id]
            raise TimeoutError(f"Tool {tool} timed out after {timeout}s")

    async def emit_thinking(self, content: str, room_id: str = None):
        target = room_id or self.current_room
        self._emit_frame("thinking", target, content)

    async def emit_message(self, content: str, room_id: str = None):
        target = room_id or self.current_room
        self._emit_frame("message", target, content)

    def _emit_frame(self, frame_type: str, room_id: str, content: str):
        self.hub_connection.send("Broadcast", [{
            "$type": frame_type,
            "SenderId": self.agent_id,
            "RoomId": room_id,
            "Content": content
        }])

    # --- Internals ---

    async def release_turn(self, room_id: str = None):
        target = room_id or self.current_room
        logging.info(f"🏳️ Releasing turn in {target}...")
        self.hub_connection.send("ReleaseTurn", [target])

    # --- Internals ---

    def _handle_signal_wrapper(self, args):
        """Bridge signalrcore thread -> asyncio loop."""
        if not args: return
        frame = args[0]
        # Schedule the coroutine on the loop
        asyncio.run_coroutine_threadsafe(self._handle_signal(frame), self.loop)

    async def _handle_signal(self, frame: AgentFrame):
        sender = frame.get("SenderId")
        content = frame.get("Content")
        
        # 1. Tool Response
        if "RequestId" in frame and sender == "GATEWAY":
            req_id = frame.get("RequestId")
            if req_id in self._pending_requests:
                future = self._pending_requests.pop(req_id)
                success = frame.get("Success", False)
                result = frame.get("ResultJson", "{}")
                
                if success:
                    future.set_result(result)
                else:
                    future.set_exception(Exception(f"Tool Limit/Error: {result}"))
            return

        # 2. Assignment
        if "NewRoomId" in frame:
            new_room = frame.get("NewRoomId")
            if self.on_assignment:
                await self.on_assignment(new_room)
            else:
                await self.join_room(new_room)
            return

        # 3. Discovery
        if "Rooms" in frame:
             if self.on_discovery:
                 await self.on_discovery(frame.get("Rooms"))
             return

        # 4. Turn Granted
        if sender == "HOST" and not content:
            if self.on_turn_granted:
                try:
                    await self.on_turn_granted()
                except Exception as e:
                    logging.error(f"❌ Error during turn execution: {e}")
                finally:
                    await self.release_turn(frame.get("RoomId"))
            return
        
        # 5. Floor Open (Reactive Turn Management)
        if "$type" in frame and (frame["$type"] == "floor_open" or frame["$type"] == "FloorOpenFrame"):
            if self.on_floor_open:
                await self.on_floor_open()
            return
        
        # 6. Output for CLI
        if content and sender != self.agent_id:
             print(f"[{sender}]: {content}")
