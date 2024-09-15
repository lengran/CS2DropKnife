# CS2DropKnife
A server-side CS2 plugin powered by CounterStrikeSharp that allows player to share their knife with others.

## How to install

Install CounterStrikeSharp first ([Installation Guide](https://docs.cssharp.dev/docs/guides/getting-started.html)). Extract the files downloaded from [Release](https://github.com/lengran/CS2DropKnife/releases) to the **game/csgo/addons/counterstrikesharp/plugins/CS2DropKnife** folder.

## How to use

As long as you have a knife equipped, type "!drop" or "!takeknife" in the chat box and you should see 5 of your knives dropped on the ground.

Note: Every player is allowed to drop knives only once in each round.

## Which version should I use?

For most of the cases, I would recommand to use [the version without chat filtering](https://github.com/lengran/CS2DropKnife/releases/download/v1.1.0/CS2DropKnife-v1.1.0.zip). You can bind css_drop or css_takeknife to your prefered key to achieve the effect of one-key-drop-knife. 

To support some drop key-bind (commands "say !drop" or "say_team !drop" issued from the game console or via a key-bind), [A version with chat filtering support](https://github.com/lengran/CS2DropKnife/releases/download/v1.1.0/CS2DropKnife-v1.1.0-with-chat-filtering.zip) is provided. This version filters all chat messages to manually call the drop knife function for you. However this might also cause performance downgrade and make the server laggy.