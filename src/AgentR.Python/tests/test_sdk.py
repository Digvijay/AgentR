import pytest
import asyncio
from unittest.mock import MagicMock, AsyncMock, patch
from agentr.client import AgentRClient
from agentr.frames import ToolResponseFrame

@pytest.fixture
def mock_hub_builder():
    with patch("agentr.client.HubConnectionBuilder") as mock_builder:
        mock_connection = MagicMock()
        mock_builder.return_value.with_url.return_value.with_hub_protocol.return_value.with_automatic_reconnect.return_value.build.return_value = mock_connection
        yield mock_builder

@pytest.fixture
def client(mock_hub_builder):
    return AgentRClient("http://localhost", "TestAgent", "token")

@pytest.mark.asyncio
async def test_invoke_tool_waits_for_response(client):
    # 1. Arrange
    mock_send = client.hub_connection.send
    
    # 2. Act (Fire async tool call)
    tool_task = asyncio.create_task(client.invoke_tool("Room1", "github", "get_issue", {}))
    
    # Allow the task to register the future
    await asyncio.sleep(0.01)
    
    # Locate the pending Request ID
    request_id = list(client._pending_requests.keys())[0]
    
    # 3. Simulate SignalR Response from Gateway
    response_frame = {
        "SenderId": "GATEWAY",
        "RoomId": "Room1",
        "RequestId": request_id, 
        "Success": True, 
        "ResultJson": '{"issue": 1}'
    }
    
    # Bridge via the wrapper to the async handler
    await client._handle_signal(response_frame)
    
    # 4. Assert
    result = await tool_task
    assert result == '{"issue": 1}'
    
    # Verify the call was made with correct structure
    args, _ = mock_send.call_args
    assert args[0] == "InvokeTool"
    payload = args[1][0]
    assert payload["RequestId"] == request_id
    assert payload["ToolName"] == "get_issue"

@pytest.mark.asyncio
async def test_invoke_tool_raises_timeout(client):
    # 1. Arrange
    client.hub_connection.send = MagicMock()
    
    # 2. Act & Assert
    with pytest.raises(TimeoutError):
        # We pass a super short timeout
        await client.invoke_tool("Room1", "git", "status", {}, timeout=0.1)

@pytest.mark.asyncio
async def test_turn_granted_executes_callback(client):
    # 1. Arrange
    callback_mock = AsyncMock()
    client.on_turn_granted = callback_mock
    
    # 2. Act
    frame = {"SenderId": "HOST", "Content": None}
    await client._handle_signal(frame)
    
    # 3. Assert
    callback_mock.assert_awaited_once()

@pytest.mark.asyncio
async def test_assignment_triggers_callback(client):
    # 1. Arrange
    callback_mock = AsyncMock()
    client.on_assignment = callback_mock
    
    # 2. Act
    frame = {"NewRoomId": "Room2"}
    await client._handle_signal(frame)
    
    # 3. Assert
    callback_mock.assert_awaited_once_with("Room2")

@pytest.mark.asyncio
async def test_assignment_joins_room_if_no_callback(client):
    # 1. Arrange
    client.hub_connection.send = MagicMock()
    
    # 2. Act
    frame = {"NewRoomId": "Room2"}
    await client._handle_signal(frame)
    
    # 3. Assert
    # Should verify it called JoinRoom
    args, _ = client.hub_connection.send.call_args
    assert args[0] == "JoinRoom"
    assert args[1][0] == "Room2"

@pytest.mark.asyncio
async def test_discovery_triggers_callback(client):
    # 1. Arrange
    callback_mock = AsyncMock()
    client.on_discovery = callback_mock
    
    # 2. Act
    rooms = [{"RoomId": "Lobby", "AgentCount": 5}]
    frame = {"Rooms": rooms}
    await client._handle_signal(frame)
    
    # 3. Assert
    callback_mock.assert_awaited_once_with(rooms)

@pytest.mark.asyncio
async def test_emit_thinking_sends_correct_frame(client):
    # 1. Arrange
    client.hub_connection.send = MagicMock()
    client.current_room = "TestRoom"
    
    # 2. Act
    await client.emit_thinking("Processing...")
    
    # 3. Assert
    args, _ = client.hub_connection.send.call_args
    assert args[0] == "Broadcast"
    payload = args[1][0]
    assert payload["$type"] == "thinking"
    assert payload["Content"] == "Processing..."
    assert payload["RoomId"] == "TestRoom"
