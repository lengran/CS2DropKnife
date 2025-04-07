# CS2DropKnife
A server-side CS2 plugin powered by CounterStrikeSharp that allows player to share their knife with others.

## How to install

Install CounterStrikeSharp first ([Installation Guide](https://docs.cssharp.dev/docs/guides/getting-started.html)). Extract the files downloaded from [Release](https://github.com/lengran/CS2DropKnife/releases) to the **game/csgo/addons/counterstrikesharp/plugins/CS2DropKnife** folder.

## How to use

As long as you have a knife equipped, type "!drop" or "!takeknife" in the chat box and you should see 5 of your knives dropped on the ground.

Note: Every player is allowed to drop knives only once in each round.

## Which version should I use?

## Customize plugin behaviors

For most of the cases, I would recommand to use the plugin as it is. But if you do wish it to behave differently, feel free to tweat the settings in **settings.json**. A template file *settings.json.example* is provided, and you can simply **rename it to settings.json to enable it**.

### Description of options
- If you want the knives to be sent directly to the teammates, set **DirectSend** to true.

- **OncePerRound** restricts players to drop knife only once in every round.

- **FreezeTimeOnly** decides whether players are only allowed to drop knife in the freeze time before rounds start.

- **ChatFiltering** is disabled defaultly. If players have bound "say !drop", this can be enabled to allow such shortcuts. But it might damage server performance. It is strongly suggested to bind "css_drop" instead of "say" or "teamsay" commands.