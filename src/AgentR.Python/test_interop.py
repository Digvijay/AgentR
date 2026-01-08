import time
import sys
from agentr import AgentRClient
from main import HUB_URL, ACCESS_TOKEN

def agent_behavior(stop_event):
    poem = ["The", "Zen", "of", "Python", "is", "beautiful", "explicit", "simple", "complex", "flat", "sparse", "readable", "special", "practical", "errors", "should", "never", "pass", "silently"]
    
    try:
        for word in poem:
            if stop_event.is_set():
                print("\n🛑 STOPPING WORK IMMEDIATELY")
                return

            # Emit thinking
            print(f"{word} ", end="", flush=True)
            time.sleep(1.0) # Slow down so we can catch it!

        print("\n✅ Finished speaking.")

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    print("🧪 Starting Python Interop Test...")
    client = AgentRClient(HUB_URL, "Python-Bridge", ACCESS_TOKEN)
    client.connect()
    
    client.on_turn_granted = agent_behavior
    
    time.sleep(2)
    print("✋ Requesting Turn...")
    client.request_turn()
    
    # Keep process alive to receive "TurnGranted" and act
    # We expect an external interrupt during this time
    time.sleep(15)
    print("\n🏁 Test Script End.")
