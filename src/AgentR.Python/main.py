import logging
import asyncio
import os
from agentr.client import AgentRClient

# Configure Logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s"
)

async def main():
    agent_id = "Python-Agent-007"
    hub_url = "http://localhost:5000/agentHub"
    token = "secret-api-key"

    client = AgentRClient(hub_url, agent_id, token)

    # --- Agent Logic ---

    async def on_turn_granted():
        """
        Main Agent Loop: Thinking -> Tool Use -> Final Answer
        """
        logging.info("🧠 Thinking process started...")
        
        # 1. Emit some thoughts
        await client.emit_thinking("I need to check the repo status.")
        await asyncio.sleep(1) # Simulate LLM latency
        
        try:
            # 2. Call a Tool (Awaitable!)
            result = await client.invoke_tool(
                room_id=client.current_room, 
                server="github", 
                tool="get_issue", 
                args={"id": 42},
                timeout=10.0
            ) 
            logging.info(f"🔧 Tool Result: {result}")
            
            await client.emit_thinking(f"I found the issue: {result}")

            # 3. Final Answer
            await client.emit_message("The issue is confirmed and being triaged.")
            
        except TimeoutError:
             await client.emit_message("I tried to check GitHub, but the tool timed out.")
        except Exception as e:
             logging.error(f"Logic Error: {e}")
             await client.emit_message("Something went wrong processing your request.")

    # Wire up callbacks
    client.on_turn_granted = on_turn_granted

    # --- Connection Sequence ---
    try:
        await client.connect()
        await client.join_room("General")
        
        # Keep the agent alive
        while True:
            await asyncio.sleep(1)
            
    except KeyboardInterrupt:
        await client.leave_room("General")
        logging.info("👋 Agent shutting down.")

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
