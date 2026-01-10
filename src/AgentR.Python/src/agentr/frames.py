from typing import TypedDict, List, Optional, Any

class AgentFrame(TypedDict):
    """Base frame for all AgentR protocol messages."""
    SenderId: str
    RoomId: str
    Timestamp: Optional[int]

class TurnGrantedFrame(AgentFrame):
    """Signal from Host giving the microphone to an agent."""
    pass

class ThinkingFrame(AgentFrame):
    """Streaming thought token."""
    Content: str

class MessageFrame(AgentFrame):
    """Final response message."""
    Content: str

class InterruptFrame(AgentFrame):
    """Signal to stop current generation immediately."""
    Reason: str

class AssignmentFrame(AgentFrame):
    """Signal to move the agent to a new room."""
    NewRoomId: str

class RoomInfo(TypedDict):
    RoomId: str
    Tags: List[str]
    AgentCount: int

class DiscoveryFrame(AgentFrame):
    """List of available rooms."""
    Rooms: List[RoomInfo]

class ToolRequestFrame(AgentFrame):
    """RPC Request for an MCP Tool."""
    RequestId: str
    ServerName: str
    ToolName: str
    ArgumentsJson: str

class ToolResponseFrame(AgentFrame):
    """RPC Response from an MCP Tool."""
    RequestId: str
    Success: bool
    ResultJson: str

class FloorOpenFrame(AgentFrame):
    """Signal that the room floor is available for turn requests."""
    pass
