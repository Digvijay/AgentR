# Python Engineering Standards

All Python code in the AgentR ecosystem must adhere to these standards to ensure concurrency, type safety, and parity with the .NET Core architecture.

## 1. AsyncIO First
- **No Threading**: Avoid `threading.Thread` or `time.sleep()`. Use native `asyncio` and `await asyncio.sleep()`.
- **Awaitable Methods**: All network I/O (tool calls, connections) must be `async def`.
- **Event Loops**: Do not manually manage event loops (`get_event_loop`). Let the runner (e.g., `asyncio.run()`, `pytest-asyncio`) handle logic.

## 2. Strong Typing with Frames
- **No Dicts**: Do not pass raw dictionaries for protocol messages.
- **TypedDict/Dataclasses**: Use `agentr.frames` definitions (e.g., `ToolRequestFrame`) for all signal handling.

## 3. Tool Invocation Pattern
- **Futures**: Tool calls must return a `Future` (or awaitable) that resolves when the gateway responds.
- **Timeouts**: All tool invocations must have a default timeout (e.g., `30.0s`) to prevent zombie tasks.

## 5. Reliability (Zombie Protection)
- **Auto-Release**: The SDK automatically handles `release_turn()` in a `finally` block.
- **Error Handling**: Allow exceptions to bubble up if needed, but ensure locks are released.

## 4. Testing
- **pytest-asyncio**: All tests must use `@pytest.mark.asyncio`.
- **Strict Mode**: Enable `asyncio_mode = strict` in `pytest.ini`.
