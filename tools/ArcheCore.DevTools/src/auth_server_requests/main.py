import json
import urllib
from urllib.parse import urlencode
from urllib.request import Request

data = {
    "Username": "testuser1",
    "Password": "123456789", }

body = json.dumps(data).encode("utf-8")

req = Request("http://127.0.0.1:3000/login",data=body,method="POST",headers={"Content-Type":"application/json"})



with urllib.request.urlopen(req) as response:
    print(response.read().decode())