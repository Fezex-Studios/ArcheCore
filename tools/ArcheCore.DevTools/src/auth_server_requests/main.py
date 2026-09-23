import json
import urllib
from urllib.parse import urlencode
from urllib.request import Request
data2 = {
    "Username": "testuser2",
    "Password": "123456789", }
data = {
    "Username": "adminuser",
    "Password": "273044001", }

body1 = json.dumps(data).encode("utf-8")
body2 = json.dumps(data2).encode("utf-8")

req = Request("http://127.0.0.1:3000/login",data=body2,method="POST",headers={"Content-Type":"application/json"})



with urllib.request.urlopen(req) as response:
    print(response.read().decode())