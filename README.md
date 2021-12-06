# MonBots
Monument Bots!

An EXPERIMENTAL basic plugin to provide for bots at Rust monuments otherwise recently evacuated and/or not yet populated by Facepunch.

Work in progress to add a basic management GUI.  The default config (not so important) and data file (very important) after initial load will be where you want to look.

For now, the data file is best edited while the plugin is unloaded.  These are the early days and improvements are coming.

SUBJECT TO CHANGE!

## Configuration
```json
{
  "Options": {
    "Default Health": 200.0,
    "Default Respawn Timer": 30.0,
    "debug": false
  },
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 1
  }
}
```

Pretty basic here.  The default should get you going.  The real meat is in the data file.

Config values will populate the data file 1:1 where they essentially match (currently health and respawn timer, which both work).

## Data file

Upon a fresh load of the plugin, various profiles are created at oxide/data/MonBots/spawnpoints.

Here is an example showing two monuments:

```json
  "Sphere Tank 0": {
    "monname": "Sphere Tank 0",
    "spawnCount": 0,
    "respawnTime": 60.0,
    "detectRange": 60.0,
    "roamRange": 140.0,
    "startHealth": 200.0,
    "invulnerable": false,
    "lootable": false,
    "dropWeapon": false,
    "hostile": false,
    "kits": null,
    "names": null,
    "pos": []
  },
  "Stables A 0": {
    "monname": "Stables A 0",
    "spawnCount": 0,
    "respawnTime": 60.0,
    "detectRange": 60.0,
    "roamRange": 140.0,
    "startHealth": 200.0,
    "invulnerable": false,
    "lootable": false,
    "dropWeapon": false,
    "hostile": false,
    "kits": null,
    "names": null,
    "pos": []
  },
```

For each monument you want to populate, you would start by setting the spawnCount to 1 or more bots.

Next, you can pick a name or set of names to be randomly assigned to the bots if desired.

You can and probably do want to assign a kit from the Kits plugin to them.  This setting can also be an array of kits if desired.

Leave pos: [] alone.  This will be populated at runtime with the spawn point of each bot in case you have to look for them, etc.

```json
  "Sphere Tank 0": {
    "monname": "Sphere Tank 0",
    "spawnCount": 3,
    "respawnTime": 60.0,
    "detectRange": 60.0,
    "roamRange": 140.0,
    "startHealth": 200.0,
    "invulnerable": false,
    "lootable": false,
    "dropWeapon": false,
    "hostile": false,
    "kits": [
      "tankbots"
    ],
    "names": {
      "Barbara",
      "Megan",
      "Lacey"
    ],
    "pos": []
  },
  "Stables A 0": {
    "monname": "Stables A 0",
    "spawnCount": 5,
    "respawnTime": 60.0,
    "detectRange": 60.0,
    "roamRange": 140.0,
    "startHealth": 200.0,
    "invulnerable": false,
    "lootable": false,
    "dropWeapon": false,
    "hostile": false,
    "kits": {
      "cowboy1",
      "cowboy2"
    },
    "names": [
      "Stabler"
    ],
    "pos": []
  },
```

You can also adjust the startHealth and other values as you like.  Not everything is working yet, but they do fight and hide and do all of the usual NPC things you might expect.

