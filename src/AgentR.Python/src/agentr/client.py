import logging
import threading
import time
from signalrcore.hub_connection_builder import HubConnectionBuilder
from signalrcore.protocol.messagepack_protocol import MessagePackHubProtocol

class AgentRClient:
    def __init__(self, hub_url, agent_id, access_token):
        self.agent_id = agent_id
        self.stop_event = threading.Event()
        self.on_turn_granted = None # Callback function

        # Configure SignalR
        self.hub_connection = HubConnectionBuilder()\
            .with_url(hub_url, options={
                "access_token_factory": lambda: access_token
            })\
            .with_hub_protocol(MessagePackHubProtocol())\
            .with_automatic_reconnect({
                "type": "raw",
                "keep_alive_interval": 10,
                "reconnect_interval": 5,
                "max_attempts": 5
            })\
            .build()

        # Wire up listeners
        self.hub_connection.on("ReceiveSignal", self._handle_signal)
        self.hub_connection.on_open(lambda: logging.info("✅ Connected to AgentR Hub!"))
        self.hub_connection.on_close(lambda: logging.info("❌ Disconnected"))

    def connect(self):
        self.hub_connection.start()
        time.sleep(1) # Give it a sec to handshake
        self.hub_connection.send("JoinRoom", ["PythonRoom"])

    def request_turn(self):
        logging.info("✋ Requesting turn...")
        self.hub_connection.send("RequestTurn", ["PythonRoom"])

    def emit_thinking(self, content):
        if self.stop_event.is_set():
            return # Don't send if interrupted
        
        # Send JSON frame (Server handles JSON/MessagePack interop automatically)
        self.hub_connection.send("Broadcast", [{
            "$type": "thinking",
            "SenderId": self.agent_id,
            "RoomId": "PythonRoom",
            "Content": content
        }])

    def release_turn(self):
        self.hub_connection.send("ReleaseTurn", ["PythonRoom"])

    def _handle_signal(self, args):
        frame = args[0]
        # Infer type based on fields (Duck Typing) since $type is missing in MessagePack
        
        # 1. Interrupt
        if "Reason" in frame:
            reason = frame.get("Reason", "Unknown")
            logging.warning(f"⚡ INTERRUPT RECEIVED: {reason}")
            self.stop_event.set()

        # 2. Thinking / Content
        elif "Content" in frame:
            sender = frame.get("SenderId")
            content = frame.get("Content")
            
            # Special case: Host sending BUSY message?
            if sender == "HOST" and "BUSY" in content:
                 logging.warning(f"⚠️ {content}")
            elif sender != self.agent_id:
                print(f"[{sender}]: {content}", end="", flush=True)

        # 3. Turn Granted (Host, No Content, No Reason)
        elif frame.get("SenderId") == "HOST":
            logging.info("🎤 Turn Granted! Starting work...")
            self.stop_event.clear()
            if self.on_turn_granted:
                threading.Thread(target=self.on_turn_granted, args=(self.stop_event,)).start()
