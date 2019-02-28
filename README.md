# FiveM Queue
A Queue system for FiveM FX Servers

<b>Installation Instructions</b>
- Download the latest release <a href="https://github.com/anderscripts/FiveM_Queue/releases/download/v1.1.0/fivemqueue.rar">here</a>
- Copy fivemqueue folder into your FX Server resources folder
- edit configuration.cfg too add identifier for your Queue Admin(s)
- start fivemqueue in your server.cfg like any other resource
- Enjoy and please report any bugs <a href="https://github.com/anderscripts/FiveM_Queue/issues">here</a>

<b>Features</b>
- Add or remove queue priority to players
- Add or remove one of three reserved slot types to players
- Configurable number of reserved slots by type
- Add or remove Permanent Banning
- Kick from session and queue
- Optional white list only mode
- Configurable reconnect grace period and loading time limit
- Queue admin commands (In-Game, RCON, or console)
- Queue admin UI panel in game to configure queue and session players
- Queue admins are permissioned in configuration.cfg by steam or license
- Customizable options and permissions included in configuration.cfg (Defaults included)

<b>Requirements</b>
- Steam and License are required for features and persistence

<b>Commands</b>
<table>
  <tr><td>Command</td><td>Paramaters</td><td>Explanation</td></tr>
  <tr><td>/q_session</td><td>None</td><td>Opens the queue admin panel displaying all players in session and providing options to configure reserved slot types, priority, and kick or ban.  If run from RCON or console will display the session information in console</td></tr>
  <tr><td>/exitgame</td><td>None</td><td>Gives players a way to give up their reconnect grace time and exit the game when finished playing</td></tr>
  <tr><td>/q_addpriority</td><td>Steam or License</td><td>Adds priority to a player<br>Also available via in game UI<br>Example: /q_addpriority 11000050888sg23<br>Example: /q_addpriority 833g50qqa4e620arq2a937312rt9b5g050d2ew54</td></tr>
  <tr><td>/q_removepriority</td><td>Steam or License</td><td>Removes priority from a player<br>Also available via in game UI<br>Example: /q_removepriority 11000050888sg23<br>Example: /q_removepriority 833g50qqa4e620arq2a937312rt9b5g050d2ew54</td></tr>
  <tr><td>/q_addreserve</td><td>Steam or License and 1 or 2 or 3</td><td>Add or change reserved slot type<br>Also available via in game UI<br>Example: /q_addreserve 11000050888sg23 1<br>Example: /q_addreserve 833g50qqa4e620arq2a937312rt9b5g050d2ew54 3</td></tr>
  <tr><td>/q_removereserve</td><td>Steam or License</td><td>Removes any reserved slot type<br>Also available via in game UI<br>Example: /q_removereserve 11000050888sg23<br>Example: /q_removereserve 833g50qqa4e620arq2a937312rt9b5g050d2ew54</td></tr>
  <tr><td>/q_addban</td><td>Steam or License</td><td>Bans a player permanently until unbanned<br>Also available via in game UI<br>Example: /q_addban 11000050888sg23<br>Example: /q_addban 833g50qqa4e620arq2a937312rt9b5g050d2ew54</td></tr>
  <tr><td>/q_removeban</td><td>Steam or License</td><td>Unbans a player<br>Example: /q_removeban 11000050888sg23<br>Example: /q_removeban 833g50qqa4e620arq2a937312rt9b5g050d2ew54</td></tr>
</table>
