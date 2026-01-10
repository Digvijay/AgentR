#!/bin/bash
# A simple mock MCP server that speaks JSON-RPC over stdin/stdout
while read -r line; do
  # Extract ID using simple grep/sed (avoiding jq dependency if not present, though jq is safer)
  # Assuming strict JSON-RPC structure from McpClient.cs
  ID=$(echo "$line" | grep -o '"id":"[^"]*"' | cut -d'"' -f4)
  METHOD=$(echo "$line" | grep -o '"method":"[^"]*"' | cut -d'"' -f4)
  
  if [[ "$METHOD" == "tools/call" ]]; then
    # Return a success response
    # {"jsonrpc":"2.0","id":"$ID","result":{"content":[{"type":"text","text":"..."}]}}
    
    RESPONSE='{"jsonrpc": "2.0", "id": "'"$ID"'", "result": {"content": [{"type": "text", "text": "{\"passed\": true, \"total\": 42, \"coverage\": \"100%\"}"}]}}'
    echo "$RESPONSE"
  fi
done
