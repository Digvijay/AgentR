import time
import logging
import sys
import jwt
from agentr import AgentRClient

# Configure Logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(message)s')

# Configuration (Match your .NET Server settings!)
HUB_URL = "http://localhost:5000/agentHub"

# --- AUTHENTICATION ---
# Generate valid JWT using the shared secret
SECRET_KEY = "ThisIsMySuperSecretKeyForAgentR_MustBe32CharsLong!"
encoded_jwt = jwt.encode(
    {"sub": "Python-Bot-01", "name": "Python-Bot-01"}, 
    SECRET_KEY, 
    algorithm="HS256"
)
ACCESS_TOKEN = encoded_jwt
print(f"🔑 Generated Auth Token: {ACCESS_TOKEN[:10]}...")

def agent_behavior(stop_event):
    """
    This represents the 'Brain'. It could be LangChain, AutoGen, or raw Python code.
    """
    poem = "The zen of python is beautiful explicit and simple...".split()
    
    try:
        for word in poem:
            # 1. Check Interrupt
            if stop_event.is_set():
                logging.error("🛑 STOPPING WORK IMMEDIATELY")
                return

            # 2. Network IO
            client.emit_thinking(f"{word} ")
            sys.stdout.write(f"{word} ")
            sys.stdout.flush()

            # 3. Simulate Think Time
            time.sleep(0.5)

        client.release_turn()
        logging.info("\n✅ Finished speaking.")

    except Exception as e:
        logging.error(f"Error: {e}")

if __name__ == "__main__":
    client = AgentRClient(HUB_URL, "Python-Bot-01", ACCESS_TOKEN)
    client.connect()

    # Define what happens when we get the mic
    client.on_turn_granted = agent_behavior

    # Main Loop
    while True:
        input("\n[Press Enter] to Request Turn...")
        client.request_turn()
