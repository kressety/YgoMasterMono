import re
import json

def remove_comments(file_path):
    with open(file_path, 'r') as file:
        content = file.read()
    content = re.sub(r'//.*', '', content)
    with open(file_path, 'w') as file:
        file.write(content)

def update_json(file_path, updates):
    with open(file_path, 'r') as file:
        data = json.load(file)
    data.update(updates)
    with open(file_path, 'w') as file:
        json.dump(data, file, indent=4)

settings_path = 'YgoMaster/Data/Settings.json'

remove_comments(settings_path)

update_json(settings_path, {
    'MultiplayerEnabled': True,
    'SessionServerIP': '0.0.0.0',
    'MultiplayerPvpClientConnectIP': 'localhost',
    'BindIP': 'http://*:{BasePort}/',
    'BaseIP': '0.0.0.0'
})
