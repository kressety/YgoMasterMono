import commentjson as json

def update_json(file_path, updates):
    with open(file_path, 'r') as file:
        data = json.load(file)
    data.update(updates)
    with open(file_path, 'w') as file:
        json.dump(data, file, indent=4)

settings_path = 'YgoMaster/Data/Settings.json'
client_settings_path = 'YgoMaster/Data/ClientSettings.json'

update_json(settings_path, {
    'MultiplayerEnabled': True,
    'SessionServerIP': '0.0.0.0',
    'MultiplayerPvpClientConnectIP': 'localhost',
    'BindIP': 'http://*:{BasePort}/',
    'BaseIP': '0.0.0.0'
})
update_json(client_settings_path, {
    'BaseIP': '0.0.0.0'
})
