using Rests;
using System.ComponentModel;
using System.Text;
using UnifierTSL.Plugins;

namespace TShockAPI.Configuration
{
    /// <summary>
    /// Settings used in the TShock configuration file
    /// </summary>
    public class TShockSettings
    {

        #region Server Settings

        /// <summary>The server password required to join the server.</summary>
        [Description("The server password required to join the server.")]
        [Newtonsoft.Json.JsonProperty("服务器密码")]
        public string ServerPassword = "";

        /// <summary>The port the server runs on.</summary>
        [Description("The port the server runs on.")]
        [Newtonsoft.Json.JsonProperty("服务器端口")]
        public int ServerPort = 7777;

        /// <summary>Maximum number of clients connected at once. If lower than Terraria's setting, the server will kick excess connections.</summary>
        [Description("Maximum number of clients connected at once.\nIf you want people to be kicked with \"Server is full\" set this to how many players you want max and then set Terraria max players to 2 higher.")]
        [Newtonsoft.Json.JsonProperty("服务器人数上限")]
        public int MaxSlots = 8;

        /// <summary>The number of reserved slots past your max server slots that can be joined by reserved players.</summary>
        [Description("The number of reserved slots past your max server slots that can be joined by reserved players.")]
        [Newtonsoft.Json.JsonProperty("服务器人满预留位")]
        public int ReservedSlots = 20;

        /// <summary>Replaces the world name during a session if UseServerName is true.</summary>
        [Description("Replaces the world name during a session if UseServerName is true.")]
        [Newtonsoft.Json.JsonProperty("服务器名称")]
        public string ServerName = "";

        /// <summary>Whether or not to use ServerName in place of the world name.</summary>
        [Description("Whether or not to use ServerName in place of the world name.")]
        [Newtonsoft.Json.JsonProperty("是否使用服务器名称")]
        public bool UseServerName = false;

        /// <summary>The path to the directory where logs should be written to.</summary>
        [Description("The path to the directory where logs should be written to.")]
        [Newtonsoft.Json.JsonProperty("服务器日志存放路径")]
        public string LogPath = "tshock/logs";

        /// <summary>Whether or not the server should output debug level messages related to system operation.</summary>
        [Description("Whether or not the server should output debug level messages related to system operation.")]
        [Newtonsoft.Json.JsonProperty("是否输出调试日志")]
        public bool DebugLogs = false;

        /// <summary>Prevents users from being able to login before they finish connecting.</summary>
        [Description("Prevents users from being able to login before they finish connecting.")]
        [Newtonsoft.Json.JsonProperty("禁止在进服前登录")]
        public bool DisableLoginBeforeJoin;

        /// <summary>Allows stacks in chests to go beyond the stack limit during world loading.</summary>
        [Description("Allows stacks in chests to go beyond the stack limit during world loading.")]
        [Newtonsoft.Json.JsonProperty("加载世界时忽略箱子堆叠超限")]
        public bool IgnoreChestStacksOnLoad = false;

        /// <summary>Allows changing of the default world tile provider.</summary>
        [Description("Allows changing of the default world tile provider. By default, you can use 'default', 'heaptile' or 'constileation'. Alternative providers have different CPU and memory usage characteristics.")]
        [Newtonsoft.Json.JsonProperty("世界图格提供器")]
        public string WorldTileProvider = "constileation";

        #endregion


        #region Backup and Save Settings

        /// <summary>Enable or disable Terraria's built-in world auto save.</summary>
        [Description("Enable or disable Terraria's built-in world auto save.")]
        [Newtonsoft.Json.JsonProperty("启用自动保存")]
        public bool AutoSave = true;

        /// <summary>Enable or disable world save announcements.</summary>
        [Description("Enable or disable world save announcements.")]
        [Newtonsoft.Json.JsonProperty("自动保存世界广播提示")]
        public bool AnnounceSave = false;

        /// <summary>Whether or not to show backup auto save messages.</summary>
        [Description("Whether or not to show backup auto save messages.")]
        [Newtonsoft.Json.JsonProperty("是否显示备份自动保存消息")]
        public bool ShowBackupAutosaveMessages = true;

        /// <summary>The interval between backups, in minutes. Backups are stored in the tshock/backups folder.</summary>
        [Description("The interval between backups, in minutes. Backups are stored in the tshock/backups folder.")]
        [Newtonsoft.Json.JsonProperty("自动备份间隔")]
        public int BackupInterval = 10;

        /// <summary>For how long backups are kept in minutes.</summary>
        [Description("For how long backups are kept in minutes.\neg. 2880 = 2 days.")]
        [Newtonsoft.Json.JsonProperty("备份保留时间")]
        public int BackupKeepFor = 240;

        /// <summary>Whether or not to save the world if the server crashes from an unhandled exception.</summary>
        [Description("Whether or not to save the world if the server crashes from an unhandled exception.")]
        [Newtonsoft.Json.JsonProperty("崩溃时保存世界")]
        public bool SaveWorldOnCrash = true;

        /// <summary>Whether or not to save the world when the last player disconnects.</summary>
        [Description("Whether or not to save the world when the last player disconnects.")]
        [Newtonsoft.Json.JsonProperty("最后玩家退出时保存世界")]
        public bool SaveWorldOnLastPlayerExit = true;

        #endregion


        #region World Settings

        /// <summary>Determines the size of invasion events. The equation for calculating invasion size = 100 + (multiplier * (number of active players > 200 hp)).</summary>
        [Description("Determines the size of invasion events.\nThe equation for calculating invasion size is 100 + (multiplier * (number of active players with greater than 200 health)).")]
        [Newtonsoft.Json.JsonProperty("事件入侵乘数")]
        public int InvasionMultiplier = 1;

        /// <summary>The default maximum number of mobs that will spawn per wave. Higher means more mobs in that wave.</summary>
        [Description("The default maximum number of mobs that will spawn per wave. Higher means more mobs in that wave.")]
        [Newtonsoft.Json.JsonProperty("默认刷怪生成上限")]
        public int DefaultMaximumSpawns = 5;

        /// <summary>The delay between waves. Lower values lead to more mobs.</summary>
        [Description("The delay between waves. Lower values lead to more mobs.")]
        [Newtonsoft.Json.JsonProperty("默认刷怪率")]
        public int DefaultSpawnRate = 600;

        /// <summary>Enables never-ending invasion events. You still need to start the event.</summary>
        [Description("Enables never ending invasion events. You still need to start the event, such as with the /invade command.")]
        [Newtonsoft.Json.JsonProperty("事件无限入侵")]
        public bool InfiniteInvasion;

        /// <summary>Sets the PvP mode. Valid types are <see cref="PvPModes"/>.</summary>
        [Description($"Sets the PvP mode. Valid types are: \"{PvPModes.Normal}\", \"{PvPModes.Always}\", \"{PvPModes.PvPWithNoTeam}\" and \"{PvPModes.Disabled}\".")]
        [Newtonsoft.Json.JsonProperty("PVP模式")]
        public string PvPMode = PvPModes.Normal;

        /// <summary>Prevents tiles from being placed within SpawnProtectionRadius of the default spawn.</summary>
        [Description("Prevents tiles from being placed within SpawnProtectionRadius of the default spawn.")]
        [Newtonsoft.Json.JsonProperty("启用出生点保护")]
        public bool SpawnProtection = false;

        /// <summary>The tile radius around the spawn tile that is protected by the SpawnProtection setting.</summary>
        [Description("The tile radius around the spawn tile that is protected by the SpawnProtection setting.")]
        [Newtonsoft.Json.JsonProperty("出生点保护范围")]
        public int SpawnProtectionRadius = 10;

        /// <summary>Enable or disable anti-cheat range checks based on distance between the player and their block placements.</summary>
        [Description("Enable or disable anti-cheat range checks based on distance between the player and their block placements.")]
        [Newtonsoft.Json.JsonProperty("启用放置物块范围检查")]
        public bool RangeChecks;

        /// <summary>Prevents non-hardcore players from connecting.</summary>
        [Description("Prevents non-hardcore players from connecting.")]
        [Newtonsoft.Json.JsonProperty("启用强制硬核角色")]
        public bool HardcoreOnly;

        /// <summary>Prevents softcore players from connecting.</summary>
        [Description("Prevents softcore players from connecting.")]
        [Newtonsoft.Json.JsonProperty("启用强制中核角色")]
        public bool MediumcoreOnly;

        /// <summary>Prevents non-softcore players from connecting.</summary>
        [Description("Prevents non-softcore players from connecting.")]
        [Newtonsoft.Json.JsonProperty("启用强制软核角色")]
        public bool SoftcoreOnly;

        /// <summary>Disables any placing, or removal of blocks.</summary>
        [Description("Disables any placing, or removal of blocks.")]
        [Newtonsoft.Json.JsonProperty("禁止建筑")]
        public bool DisableBuild;

        /// <summary>If enabled, hardmode will not be activated by the Wall of Flesh or the /starthardmode command.</summary>
        [Description("If enabled, hardmode will not be activated by the Wall of Flesh or the /starthardmode command.")]
        [Newtonsoft.Json.JsonProperty("禁止困难模式")]
        public bool DisableHardmode;

        /// <summary>Prevents the dungeon guardian from being spawned while sending players to their spawn point instead.</summary>
        [Description("Prevents the dungeon guardian from being spawned while sending players to their spawn point instead.")]
        [Newtonsoft.Json.JsonProperty("禁止生成地牢守卫")]
        public bool DisableDungeonGuardian;

        /// <summary>Disables clown bomb projectiles from spawning.</summary>
        [Description("Disables clown bomb projectiles from spawning.")]
        [Newtonsoft.Json.JsonProperty("禁止血月小丑炸弹")]
        public bool DisableClownBombs;

        /// <summary>Disables snow ball projectiles from spawning.</summary>
        [Description("Disables snow ball projectiles from spawning.")]
        [Newtonsoft.Json.JsonProperty("禁止雪人雪块弹幕")]
        public bool DisableSnowBalls;

        /// <summary>Disables tombstone dropping during death for all players.</summary>
        [Description("Disables tombstone dropping during death for all players.")]
        [Newtonsoft.Json.JsonProperty("禁止玩家死亡生成墓碑")]
        public bool DisableTombstones;

        /// <summary>
        /// Disables Skeletron Prime Bombs from spawning, useful for preventing unwanted world destruction on for the worthy seed world.
        /// </summary>
        [Description("Disables Skeletron Prime Bombs from spawning, useful for preventing unwanted world destruction on for the worthy seed world.")]
        [Newtonsoft.Json.JsonProperty("禁止机械骷髅王炸弹")]
        public bool DisablePrimeBombs;

        /// <summary>Forces the world time to be normal, day, or night.</summary>
        [Description("Forces the world time to be normal, day, or night.")]
        [Newtonsoft.Json.JsonProperty("强制世界时间")]
        public string ForceTime = "normal";

        /// <summary>Disables the effect of invisibility potions while PvP is enabled by turning the player visible to the other clients.</summary>
        [Description("Disables the effect of invisibility potions while PvP is enabled by turning the player visible to the other clients.")]
        [Newtonsoft.Json.JsonProperty("禁止PVP隐身药水")]
        public bool DisableInvisPvP;

        /// <summary>The maximum distance, in tiles, that disabled players can move from.</summary>
        [Description("The maximum distance, in tiles, that disabled players can move from.")]
        [Newtonsoft.Json.JsonProperty("未登录玩家允许移动的最大距离")]
        public int MaxRangeForDisabled = 10;

        /// <summary>Whether or not region protection should apply to chests.</summary>
        [Description("Whether or not region protection should apply to chests.")]
        [Newtonsoft.Json.JsonProperty("保护区域箱子")]
        public bool RegionProtectChests;

        /// <summary>Whether or not region protection should apply to gem locks.</summary>
        [Description("Whether or not region protection should apply to gem locks.")]
        [Newtonsoft.Json.JsonProperty("保护区域内宝石锁")]
        public bool RegionProtectGemLocks = true;

        /// <summary>Ignores checks to see if a player 'can' update a projectile.</summary>
        [Description("Ignores checks to see if a player 'can' update a projectile.")]
        [Newtonsoft.Json.JsonProperty("忽略检查玩家弹幕更新")]
        public bool IgnoreProjUpdate = false;

        /// <summary>Ignores checks to see if a player 'can' kill a projectile.</summary>
        [Description("Ignores checks to see if a player 'can' kill a projectile.")]
        [Newtonsoft.Json.JsonProperty("忽略检查玩家弹幕销毁")]
        public bool IgnoreProjKill = false;

        /// <summary>Allows players to break temporary tiles (grass, pots, etc) where they cannot usually build.</summary>
        [Description("Allows players to break temporary tiles (grass, pots, etc) where they cannot usually build.")]
        [Newtonsoft.Json.JsonProperty("允许玩家破坏易碎方块")]
        public bool AllowCutTilesAndBreakables;

        /// <summary>Allows ice placement even where a user cannot usually build.</summary>
        [Description("Allows ice placement even where a user cannot usually build.")]
        [Newtonsoft.Json.JsonProperty("允许玩家保护区域释放冰块")]
        public bool AllowIce;

        /// <summary>Allows the crimson to spread when a world is in hardmode.</summary>
        [Description("Allows the crimson to spread when a world is in hardmode.")]
        [Newtonsoft.Json.JsonProperty("允许猩红蔓延")]
        public bool AllowCrimsonCreep = true;

        /// <summary>Allows the corruption to spread when a world is in hardmode.</summary>
        [Description("Allows the corruption to spread when a world is in hardmode.")]
        [Newtonsoft.Json.JsonProperty("允许腐化蔓延")]
        public bool AllowCorruptionCreep = true;

        /// <summary>Allows the hallow to spread when a world is in hardmode.</summary>
        [Description("Allows the hallow to spread when a world is in hardmode.")]
        [Newtonsoft.Json.JsonProperty("允许神圣蔓延")]
        public bool AllowHallowCreep = true;

        /// <summary>How many NPCs a statue can spawn within 200 pixels(?) before it stops spawning.</summary>
        [Description("How many NPCs a statue can spawn within 200 pixels(?) before it stops spawning.\nDefault = 3.")]
        [Newtonsoft.Json.JsonProperty("雕像 200 像素(12.5格)内同类NPC上限")]
        public int StatueSpawn200 = 3;

        /// <summary>How many NPCs a statue can spawn within 600 pixels(?) before it stops spawning.</summary>
        [Description("How many NPCs a statue can spawn within 600 pixels(?) before it stops spawning.\nDefault = 6.")]
        [Newtonsoft.Json.JsonProperty("雕像 600 像素(37.5格)内同类NPC上限")]
        public int StatueSpawn600 = 6;

        /// <summary>How many NPCs a statue can spawn before it stops spawning.</summary>
        [Description("How many NPCs a statue can spawn before it stops spawning.\nDefault = 10.")]
        [Newtonsoft.Json.JsonProperty("整个世界该NPC的雕像生成上限")]
        public int StatueSpawnWorld = 10;

        /// <summary>Prevent banned items from being spawned or given with commands.</summary>
        [Description("Prevent banned items from being spawned or given with commands.")]
        [Newtonsoft.Json.JsonProperty("阻止禁用物品生成或指令获取")]
        public bool PreventBannedItemSpawn = false;

        /// <summary>Prevent players from interacting with the world while they are dead.</summary>
        [Description("Prevent players from interacting with the world while they are dead.")]
        [Newtonsoft.Json.JsonProperty("阻止玩家死后与世界互动")]
        public bool PreventDeadModification = true;

        /// <summary>Prevents players from placing tiles with an invalid style.</summary>
        [Description("Prevents players from placing tiles with an invalid style.")]
        [Newtonsoft.Json.JsonProperty("阻止玩家放置无效特殊值方块")]
        public bool PreventInvalidPlaceStyle = true;

        /// <summary>Forces Christmas-only events to occur all year.</summary>
        [Description("Forces Christmas-only events to occur all year.")]
        [Newtonsoft.Json.JsonProperty("强制圣诞节")]
        public bool ForceXmas = false;

        /// <summary>Forces Halloween-only events to occur all year.</summary>
        [Description("Forces Halloween-only events to occur all year.")]
        [Newtonsoft.Json.JsonProperty("强制万圣节")]
        public bool ForceHalloween = false;

        /// <summary>Allows groups on the banned item allowed list to spawn banned items even if PreventBannedItemSpawn is set to true.</summary>
        [Description("Allows groups on the banned item allowed list to spawn banned items even if PreventBannedItemSpawn is set to true.")]
        [Newtonsoft.Json.JsonProperty("允许可使用禁用物品的组生成禁用物品")]
        public bool AllowAllowedGroupsToSpawnBannedItems = false;

        /// <summary>The number of seconds a player must wait before being respawned. Valid range: 0 (default) to 15 seconds. Use at your own risk.</summary>
        [Description("The number of seconds a player must wait before being respawned. Valid range: 0 (default) to 15 seconds. Use at your own risk.")]
        [Newtonsoft.Json.JsonProperty("玩家复活时间")]
        public int RespawnSeconds = 0;

        /// <summary>The number of seconds a player must wait before being respawned if there is a boss nearby. Valid range: 0 (default) to 30 seconds. Use at your own risk.</summary>
        [Description("The number of seconds a player must wait before being respawned if there is a boss nearby. Valid range: 0 (default) to 30 seconds. Use at your own risk.")]
        [Newtonsoft.Json.JsonProperty("玩家BOSS战复活时间")]
        public int RespawnBossSeconds = 0;

        /// <summary>Whether or not to announce boss spawning or invasion starts.</summary>
        [Description("Whether or not to announce boss spawning or invasion starts.")]
        [Newtonsoft.Json.JsonProperty("不显示召唤BOSS或事件入侵的玩家")]
        public bool AnonymousBossInvasions = true;

        /// <summary>The maximum HP a player can have, before equipment buffs.</summary>
        [Description("The maximum HP a player can have, before equipment buffs.")]
        [Newtonsoft.Json.JsonProperty("玩家血量上限")]
        public int MaxHP = 500;

        /// <summary>The maximum MP a player can have, before equipment buffs.</summary>
        [Description("The maximum MP a player can have, before equipment buffs.")]
        [Newtonsoft.Json.JsonProperty("玩家蓝量上限")]
        public int MaxMP = 200;

        /// <summary>Determines the range in tiles that a bomb can affect tiles from detonation point.</summary>
        [Description("Determines the range in tiles that a bomb can affect tiles from detonation point.")]
        [Newtonsoft.Json.JsonProperty("爆炸影响范围")]
        public int BombExplosionRadius = 5;

        /// <summary>If set to true, items given to players will be inserted directly into their inventory. Requires SSC. Otherwise, items given to players will spawn as dropped items.</summary>
        [Description("If set to true, items given to players will be inserted directly into their inventory. Requires SSC. Otherwise, items given to players will spawn as dropped items. Experimental feature. May not work correctly or result in item loss.")]
        [Newtonsoft.Json.JsonProperty("给予物品直接插入玩家背包(需SSC)")]
        public bool GiveItemsDirectly = false;

        #endregion


        #region Login and Ban Settings

        /// <summary>The default group name to place newly registered users under.</summary>
        [Description("The default group name to place newly registered users under.")]
        [Newtonsoft.Json.JsonProperty("玩家注册后的用户组")]
        public string DefaultRegistrationGroupName = "default";

        /// <summary>The default group name to place unregistered players under.</summary>
        [Description("The default group name to place unregistered players under.")]
        [Newtonsoft.Json.JsonProperty("玩家注册前的用户组")]
        public string DefaultGuestGroupName = "guest";

        /// <summary>Remembers where a player left off, based on their IP. Does not persist through server restarts.</summary>
        [Description("Remembers where a player left off, based on their IP. Does not persist through server restarts.\neg. When you try to disconnect, and reconnect to be automatically placed at spawn, you'll be at your last location.")]
        [Newtonsoft.Json.JsonProperty("进服传回离线时的位置")]
        public bool RememberLeavePos;

        /// <summary>Number of failed login attempts before kicking the player.</summary>
        [Description("Number of failed login attempts before kicking the player.")]
        [Newtonsoft.Json.JsonProperty("尝试登录次数上限")]
        public int MaximumLoginAttempts = 3;

        /// <summary>Whether or not to kick mediumcore players on death.</summary>
        [Description("Whether or not to kick mediumcore players on death.")]
        [Newtonsoft.Json.JsonProperty("踢出死亡后的中核玩家")]
        public bool KickOnMediumcoreDeath;

        /// <summary>The reason given if kicking a mediumcore players on death.</summary>
        [Description("The reason given if kicking a mediumcore players on death.")]
        [Newtonsoft.Json.JsonProperty("踢出中核玩家时的提示语")]
        public string MediumcoreKickReason = "Death results in a kick";

        /// <summary>Whether or not to ban mediumcore players on death.</summary>
        [Description("Whether or not to ban mediumcore players on death.")]
        [Newtonsoft.Json.JsonProperty("封禁死亡后的中核玩家")]
        public bool BanOnMediumcoreDeath;

        /// <summary>The reason given if banning a mediumcore player on death.</summary>
        [Description("The reason given if banning a mediumcore player on death.")]
        [Newtonsoft.Json.JsonProperty("封禁中核玩家时的提示语")]
        public string MediumcoreBanReason = GetString("Death results in a ban");

        /// <summary>Disables IP bans by default, if no arguments are passed to the ban command.</summary>
        [Description("Disables IP bans by default, if no arguments are passed to the ban command.")]
        [Newtonsoft.Json.JsonProperty("关闭默认封禁IP")]
        public bool DisableDefaultIPBan;

        /// <summary>Enable or disable the whitelist based on IP addresses in the whitelist.txt file.</summary>
        [Description("Enable or disable the whitelist based on IP addresses in the whitelist.txt file.")]
        [Newtonsoft.Json.JsonProperty("启用IP白名单")]
        public bool EnableWhitelist;

        /// <summary>The reason given when kicking players for not being on the whitelist.</summary>
        [Description("The reason given when kicking players for not being on the whitelist.")]
        [Newtonsoft.Json.JsonProperty("不在白名单被踢出的信息")]
        public string WhitelistKickReason = GetString("You are not on the whitelist.");

        /// <summary>The reason given when kicking players that attempt to join while the server is full.</summary>
        [Description("The reason given when kicking players that attempt to join while the server is full.")]
        [Newtonsoft.Json.JsonProperty("服务器人满提示信息")]
        public string ServerFullReason = GetString("Server is full");

        /// <summary>The reason given when kicking players that attempt to join while the server is full with no reserved slots available.</summary>
        [Description("The reason given when kicking players that attempt to join while the server is full with no reserved slots available.")]
        [Newtonsoft.Json.JsonProperty("服务器人满且没有保留位的提示信息")]
        public string ServerFullNoReservedReason = GetString("Server is full. No reserved slots open.");

        /// <summary>Whether or not to kick hardcore players on death.</summary>
        [Description("Whether or not to kick hardcore players on death.")]
        [Newtonsoft.Json.JsonProperty("踢出死亡后的硬核玩家")]
        public bool KickOnHardcoreDeath;

        /// <summary>The reason given when kicking hardcore players on death.</summary>
        [Description("The reason given when kicking hardcore players on death.")]
        [Newtonsoft.Json.JsonProperty("踢出硬核玩家时的提示语")]
        public string HardcoreKickReason = GetString("Death results in a kick");

        /// <summary>Whether or not to ban hardcore players on death.</summary>
        [Description("Whether or not to ban hardcore players on death.")]
        [Newtonsoft.Json.JsonProperty("封禁死亡后的硬核玩家")]
        public bool BanOnHardcoreDeath;

        /// <summary>The reason given when banning hardcore players on death.</summary>
        [Description("The reason given when banning hardcore players on death.")]
        [Newtonsoft.Json.JsonProperty("封禁硬核玩家时的提示语")]
        public string HardcoreBanReason = GetString("Death results in a ban");

        /// <summary>If GeoIP is enabled, this will kick users identified as being under a proxy.</summary>
        [Description("If GeoIP is enabled, this will kick users identified as being under a proxy.")]
        [Newtonsoft.Json.JsonProperty("踢出代理IP用户")]
        public bool KickProxyUsers = true;

        /// <summary>Require all players to register or login before being allowed to play.</summary>
        [Description("Require all players to register or login before being allowed to play.")]
        [Newtonsoft.Json.JsonProperty("用户必须登录")]
        public bool RequireLogin;

        /// <summary>Allows users to login to any account even if the username doesn't match their character name.</summary>
        [Description("Allows users to login to any account even if the username doesn't match their character name.")]
        [Newtonsoft.Json.JsonProperty("允许玩家登录账号与角色名不符")]
        public bool AllowLoginAnyUsername = true;

        /// <summary>Allows users to register a username that doesn't necessarily match their character name.</summary>
        [Description("Allows users to register a username that doesn't necessarily match their character name.")]
        [Newtonsoft.Json.JsonProperty("允许玩家注册账号与角色名不符")]
        public bool AllowRegisterAnyUsername;

        /// <summary>The minimum password length for new user accounts. Can never be lower than 4.</summary>
        [Description("The minimum password length for new user accounts. Can never be lower than 4.")]
        [Newtonsoft.Json.JsonProperty("密码最少长度")]
        public int MinimumPasswordLength = 4;

        /// <summary>Determines the BCrypt work factor to use. If increased, all passwords will be upgraded to new work-factor on verify.
        /// The number of computational rounds is 2^n. Increase with caution. Range: 5-31.</summary>
        [Description("Determines the BCrypt work factor to use. If increased, all passwords will be upgraded to new work-factor on verify. The number of computational rounds is 2^n. Increase with caution. Range: 5-31.")]
        [Newtonsoft.Json.JsonProperty("使用的BCrypt工作因子")]
        public int BCryptWorkFactor = 7;

        /// <summary>Prevents users from being able to login with their client UUID.</summary>
        [Description("Prevents users from being able to login with their client UUID.")]
        [Newtonsoft.Json.JsonProperty("禁止UUID自动登录")]
        public bool DisableUUIDLogin;

        /// <summary>Kick clients that don't send their UUID to the server.</summary>
        [Description("Kick clients that don't send their UUID to the server.")]
        [Newtonsoft.Json.JsonProperty("踢出不发送UUID到服务器的玩家")]
        public bool KickEmptyUUID = true;

        /// <summary>Disables a player if this number of tiles is painted within 1 second.</summary>
        [Description("Disables a player if this number of tiles is painted within 1 second.")]
        [Newtonsoft.Json.JsonProperty("1秒内刷油漆的格数上限")]
        public int TilePaintThreshold = 200;

        /// <summary>Whether or not to kick users when they surpass the TilePaint threshold.</summary>
        [Description("Whether or not to kick users when they surpass the TilePaint threshold.")]
        [Newtonsoft.Json.JsonProperty("踢出1秒内刷油漆超出格数上限的玩家")]
        public bool KickOnTilePaintThresholdBroken = false;

        /// <summary>The maximum damage a player/NPC can inflict.</summary>
        [Description("The maximum damage a player/NPC can inflict.")]
        [Newtonsoft.Json.JsonProperty("玩家最大伤害上限")]
        public int MaxDamage = 20000;

        /// <summary>The maximum damage a projectile can inflict.</summary>
        [Description("The maximum damage a projectile can inflict.")]
        [Newtonsoft.Json.JsonProperty("玩家最大弹幕伤害上限")]
        public int MaxProjDamage = 20000;

        /// <summary>Whether or not to kick users when they surpass the MaxDamage threshold.</summary>
        [Description("Whether or not to kick users when they surpass the MaxDamage threshold.")]
        [Newtonsoft.Json.JsonProperty("踢出超出伤害上限的玩家")]
        public bool KickOnDamageThresholdBroken = false;

        /// <summary>Disables a player and reverts their actions if this number of tile kills is exceeded within 1 second.</summary>
        [Description("Disables a player and reverts their actions if this number of tile kills is exceeded within 1 second.")]
        [Newtonsoft.Json.JsonProperty("1秒内破坏方块的格数上限")]
        public int TileKillThreshold = 200;

        /// <summary>Whether or not to kick users when they surpass the TileKill threshold.</summary>
        [Description("Whether or not to kick users when they surpass the TileKill threshold.")]
        [Newtonsoft.Json.JsonProperty("踢出1秒内破坏方块超出格数上限的玩家")]
        public bool KickOnTileKillThresholdBroken = false;

        /// <summary>Disables a player and reverts their actions if this number of tile places is exceeded within 1 second.</summary>
        [Description("Disables a player and reverts their actions if this number of tile places is exceeded within 1 second.")]
        [Newtonsoft.Json.JsonProperty("1秒内放置方块的格数上限")]
        public int TilePlaceThreshold = 200;

        /// <summary>Whether or not to kick users when they surpass the TilePlace threshold.</summary>
        [Description("Whether or not to kick users when they surpass the TilePlace threshold.")]
        [Newtonsoft.Json.JsonProperty("踢出1秒内放置方块超出格数上限的玩家")]
        public bool KickOnTilePlaceThresholdBroken = false;

        /// <summary>Disables a player if this number of liquid sets is exceeded within 1 second.</summary>
        [Description("Disables a player if this number of liquid sets is exceeded within 1 second.")]
        [Newtonsoft.Json.JsonProperty("1秒内放置液体的格数上限")]
        public int TileLiquidThreshold = 200;

        /// <summary>Whether or not to kick users when they surpass the TileLiquid threshold.</summary>
        [Description("Whether or not to kick users when they surpass the TileLiquid threshold.")]
        [Newtonsoft.Json.JsonProperty("踢出1秒内放置液体超出格数上限的玩家")]
        public bool KickOnTileLiquidThresholdBroken = false;

        /// <summary>Whether or not to ignore shrapnel from crystal bullets for the projectile threshold count.</summary>
        [Description("Whether or not to ignore shrapnel from crystal bullets for the projectile threshold count.")]
        [Newtonsoft.Json.JsonProperty("弹幕数量是否包含水晶子弹")]
        public bool ProjIgnoreShrapnel = true;

        /// <summary>Disable a player if this number of projectiles is created within 1 second.</summary>
        [Description("Disable a player if this number of projectiles is created within 1 second.")]
        [Newtonsoft.Json.JsonProperty("1秒内释放弹幕的数量上限")]
        public int ProjectileThreshold = 200;

        /// <summary>Whether or not to kick users when they surpass the Projectile threshold.</summary>
        [Description("Whether or not to kick users when they surpass the Projectile threshold.")]
        [Newtonsoft.Json.JsonProperty("踢出1秒内释放弹幕超出数量上限的玩家")]
        public bool KickOnProjectileThresholdBroken = false;

        /// <summary>Disables a player if this number of HealOtherPlayer packets is sent within 1 second.</summary>
        [Description("Disables a player if this number of HealOtherPlayer packets is sent within 1 second.")]
        [Newtonsoft.Json.JsonProperty("1秒内治疗其他玩家的数值上限")]
        public int HealOtherThreshold = 200;

        /// <summary>Whether or not to kick users when they surpass the HealOther threshold.</summary>
        [Description("Whether or not to kick users when they surpass the HealOther threshold.")]
        [Newtonsoft.Json.JsonProperty("踢出1秒内治疗其他玩家超出数值上限的玩家")]
        public bool KickOnHealOtherThresholdBroken = false;

        /// <summary>Whether or not the server should suppress build permission failure warnings from regions, spawn point, or server edit failure.</summary>
        [Description("Whether or not the server should suppress build permission failure warnings from regions, spawn point, or server edit failure.")]
        [Newtonsoft.Json.JsonProperty("不提示受保护区域无权建筑信息")]
        public bool SuppressPermissionFailureNotices = false;

        /// <summary>Prohibit the use of Zenith projectile with different objects instead of weapons.</summary>
        [Description("Prohibit the use of Zenith projectile with different objects instead of weapons.")]
        [Newtonsoft.Json.JsonProperty("禁止修改的天顶剑")]
        public bool DisableModifiedZenith = false;

        /// <summary>Allows you to disable or enable protection against creating custom messages with death. Created for developers who came up with a more original solution to this problem.</summary>
        [Description("Allows you to disable or enable protection against creating custom messages with death. Created for developers who came up with a more original solution to this problem.")]
        [Newtonsoft.Json.JsonProperty("禁用自定义死亡信息")]
        public bool DisableCustomDeathMessages = true;

        /// <summary>Allows players to use [ct:] tags in chat.</summary>
        [Description("Allows players to use [ct:] tags in chat. Note: invalid [ct:] tags can crash mobile clients.")]
        [Newtonsoft.Json.JsonProperty("允许玩家使用聊天标签[ct:]（可能导致移动端崩溃）")]
        public bool AllowCtTag = false;
        #endregion


        #region Chat Settings

        /// <summary>Specifies which string starts a command.
        /// Note: Will not function properly if the string length is bigger than 1.</summary>
        [Description("Specifies which string starts a command.\nNote: Will not function properly if the string length is bigger than 1.")]
        [Newtonsoft.Json.JsonProperty("指令前缀")]
        public string CommandSpecifier = "/";

        /// <summary>Specifies which string starts a command silently.
        /// Note: Will not function properly if the string length is bigger than 1.</summary>
        [Description("Specifies which string starts a command silently.\nNote: Will not function properly if the string length is bigger than 1.")]
        [Newtonsoft.Json.JsonProperty("隐藏指令前缀")]
        public string CommandSilentSpecifier = ".";

        /// <summary>The maximum allowed length for chat messages. Valid range: 250 characters to 2000 characters.</summary>
        [Description("The maximum allowed length for chat messages. Valid range: 250 characters to 2000 characters.")]
        [Newtonsoft.Json.JsonProperty("聊天消息最大长度")]
        public int MaximumChatMessageLength = 500;

        /// <summary>If a chat message that exceeds MaximumChatMessageLength should be truncated or rejected.</summary>
        [Description("If a chat message that exceeds MaximumChatMessageLength should be truncated or rejected.")]
        [Newtonsoft.Json.JsonProperty("截断过长的聊天消息")]
        public bool TruncateExcessiveChatMessages = false;

        /// <summary>Disables sending logs as messages to players with the log permission.</summary>
        [Description("Disables sending logs as messages to players with the log permission.")]
        [Newtonsoft.Json.JsonProperty("不将日志作为聊天信息发送给有日志权限的玩家")]
        public bool DisableSpewLogs = true;

        /// <summary>Prevents OnSecondUpdate checks from writing to the log file.</summary>
        [Description("Prevents OnSecondUpdate checks from writing to the log file.")]
        [Newtonsoft.Json.JsonProperty("不将每秒的更新检查写入日志")]
        public bool DisableSecondUpdateLogs = false;

        /// <summary>The chat color for the superadmin group.</summary>
        [Description("The chat color for the superadmin group.\n#.#.# = Red/Blue/Green\nMax value: 255")]
        [Newtonsoft.Json.JsonProperty("超级管理员聊天颜色(RGB)")]
        public int[] SuperAdminChatRGB = { 255, 255, 255 };

        /// <summary>The superadmin chat prefix.</summary>
        [Description("The superadmin chat prefix.")]
        [Newtonsoft.Json.JsonProperty("超管的聊天前缀")]
        public string SuperAdminChatPrefix = GetString("(Super Admin) ");

        /// <summary>The superadmin chat suffix.</summary>
        [Description("The superadmin chat suffix.")]
        [Newtonsoft.Json.JsonProperty("超管的聊天后缀")]
        public string SuperAdminChatSuffix = "";

        /// <summary>Whether or not to announce a player's geographic location on join, based on their IP.</summary>
        [Description("Whether or not to announce a player's geographic location on join, based on their IP.")]
        [Newtonsoft.Json.JsonProperty("显示加入服务器的玩家IP地理位置")]
        public bool EnableGeoIP = true;

        /// <summary>Displays a player's IP on join to users with the log permission.</summary>
        [Description("Displays a player's IP on join to users with the log permission.")]
        [Newtonsoft.Json.JsonProperty("向有日志权限的管理显示进入玩家的IP")]
        public bool DisplayIPToAdmins = true;

        /// <summary>Changes in-game chat format: {0} = Group Name, {1} = Group Prefix, {2} = Player Name, {3} = Group Suffix, {4} = Chat Message.</summary>
        [Description("Changes in-game chat format: {0} = Group Name, {1} = Group Prefix, {2} = Player Name, {3} = Group Suffix, {4} = Chat Message.")]
        [Newtonsoft.Json.JsonProperty("聊天格式")]
        public string ChatFormat = "{1}{2}{3}: {4}";

        /// <summary>Changes the player name when using chat above heads. Starts with a player name wrapped in brackets, as per Terraria's formatting.\nSame formatting as ChatFormat without the message.</summary>
        [Description("Changes the player name when using chat above heads. Starts with a player name wrapped in brackets, as per Terraria's formatting.\nSame formatting as ChatFormat without the message.")]
        [Newtonsoft.Json.JsonProperty("头顶聊天格式")]
        public string ChatAboveHeadsFormat = "{2}";

        /// <summary>Whether or not to display chat messages above players' heads.</summary>
        [Description("Whether or not to display chat messages above players' heads.")]
        [Newtonsoft.Json.JsonProperty("是否在玩家头顶显示聊天消息")]
        public bool EnableChatAboveHeads = true;

        /// <summary>The RGB values used for the color of broadcast messages.</summary>
        [Description("The RGB values used for the color of broadcast messages.\n#.#.# = Red/Blue/Green\nMax value: 255")]
        [Newtonsoft.Json.JsonProperty("系统广播文字颜色(RGB)")]
        public int[] BroadcastRGB = { 127, 255, 212 };

        #endregion


        #region MySQL Settings

        /// <summary>The type of database to use when storing data (either "sqlite" or "mysql").</summary>
        [Description("The type of database to use when storing data (either \"sqlite\", \"mysql\" or \"postgres\").")]
        [Newtonsoft.Json.JsonProperty("数据库类型")]
        public string StorageType = "sqlite";

        /// <summary>The SQLite connection string. Overrides SqliteDBPath when provided.</summary>
        [Description("The SQLite connection string. Overrides SqliteDBPath when provided.")]
        [Newtonsoft.Json.JsonProperty("SQLite 连接字符串")]
        public string SqliteConnectionString = "";

        /// <summary>The path of sqlite db.</summary>
        [Description("The path of sqlite db.")]
        [Newtonsoft.Json.JsonProperty("数据库路径")]
        public string SqliteDBPath = "tshock.sqlite";

        /// <summary>The MySQL connection string. Overrides host/db/user/password when provided.</summary>
        [Description("The MySQL connection string. Overrides MySqlHost, MySqlDbName, MySqlUsername and MySqlPassword when provided.")]
        [Newtonsoft.Json.JsonProperty("MySQL 连接字符串")]
        public string MySqlConnectionString = "";

        /// <summary>The MySQL hostname and port to Direct connections to.</summary>
        [Description("The MySQL hostname and port to Direct connections to.")]
        [Newtonsoft.Json.JsonProperty("Mysql连接的ip和端口")]
        public string MySqlHost = "localhost:3306";

        /// <summary>The database name to connect to when using MySQL as the database type.</summary>
        [Description("The database name to connect to when using MySQL as the database type.")]
        [Newtonsoft.Json.JsonProperty("Mysql的数据库名称")]
        public string MySqlDbName = "";

        /// <summary>The username used when connecting to a MySQL database.</summary>
        [Description("The username used when connecting to a MySQL database.")]
        [Newtonsoft.Json.JsonProperty("Mysql的用户名")]
        public string MySqlUsername = "";

        /// <summary>The password used when connecting to a MySQL database.</summary>
        [Description("The password used when connecting to a MySQL database.")]
        [Newtonsoft.Json.JsonProperty("Mysql的密码")]
        public string MySqlPassword = "";

        ///<summary>The Postgres hostname and port to Direct connections to.</summary>
        [Description("The Postgres hostname and port to Direct connections to.")]
        [Newtonsoft.Json.JsonProperty("Postgres连接的ip和端口")]
        public string PostgresHost = "";

        /// <summary>The database name to connect to when using Postgres as the database type.</summary>
        [Description("The database name to connect to when using Postgres as the database type.")]
        [Newtonsoft.Json.JsonProperty("Postgres的数据库名称")]
        public string PostgresDbName = "";

        /// <summary>The username used when connecting to a Postgres database.</summary>
        [Description("The username used when connecting to a Postgres database.")]
        [Newtonsoft.Json.JsonProperty("Postgres的用户名")]
        public string PostgresUsername = "";

        /// <summary>The password used when connecting to a Postgres database.</summary>
        [Description("The password used when connecting to a Postgres database.")]
        [Newtonsoft.Json.JsonProperty("Postgres的密码")]
        public string PostgresPassword = "";

        /// <summary>The Postgres connection string. Overrides host/db/user/password when provided.</summary>
        [Description("The Postgres connection string. Overrides PostgresHost, PostgresDbName, PostgresUsername and PostgresPassword when provided.")]
        [Newtonsoft.Json.JsonProperty("PostgreSQL 连接字符串")]
        public string PostgresConnectionString = "";

        /// <summary>Whether or not to save logs to the SQL database instead of a text file.</summary>
        [Description("Whether or not to save logs to the SQL database instead of a text file.\nDefault = false.")]
        [Newtonsoft.Json.JsonProperty("是否把日志存入数据库")]
        public bool UseSqlLogs = false;

        /// <summary>Number of times the SQL log must fail to insert logs before falling back to the text log.</summary>
        [Description("Number of times the SQL log must fail to insert logs before falling back to the text log.")]
        [Newtonsoft.Json.JsonProperty("Sql日志连接失败指定次数后变回文本日志")]
        public int RevertToTextLogsOnSqlFailures = 10;

        #endregion


        #region REST API Settings

        /// <summary>Enable or disable the REST API.</summary>
        [Description("Enable or disable the REST API.")]
        [Newtonsoft.Json.JsonProperty("开启 Rest API")]
        public bool RestApiEnabled;

        /// <summary>The port used by the REST API.</summary>
        [Description("The port used by the REST API.")]
        [Newtonsoft.Json.JsonProperty("Rest的端口")]
        public int RestApiPort = 7878;

        /// <summary>Whether or not to log REST API connections.</summary>
        [Description("Whether or not to log REST API connections.")]
        [Newtonsoft.Json.JsonProperty("记录Rest连线")]
        public bool LogRest = false;

        /// <summary>Whether or not to require token authentication to use the public REST API endpoints.</summary>
        [Description("Whether or not to require token authentication to use the public REST API endpoints.")]
        [Newtonsoft.Json.JsonProperty("开启对Rest的权限认证")]
        public bool EnableTokenEndpointAuthentication;

        /// <summary>The maximum REST requests in the bucket before denying requests. Minimum value is 5.</summary>
        [Description("The maximum REST requests in the bucket before denying requests. Minimum value is 5.")]
        [Newtonsoft.Json.JsonProperty("REST 每个周期最大请求数")]
        public int RESTMaximumRequestsPerInterval = 5;

        /// <summary>How often in minutes the REST requests bucket is decreased by one. Minimum value is 1 minute.</summary>
        [Description("How often in minutes the REST requests bucket is decreased by one. Minimum value is 1 minute.")]
        [Newtonsoft.Json.JsonProperty("REST 请求令牌补充间隔(分钟)")]
        public int RESTRequestBucketDecreaseIntervalMinutes = 1;

        /// <summary>A dictionary of REST tokens that external applications may use to make queries to your server.</summary>
        [Description("A dictionary of REST tokens that external applications may use to make queries to your server.")]
        [Newtonsoft.Json.JsonProperty("Rest外部应用令牌字典")]
        public Dictionary<string, SecureRest.TokenData> ApplicationRestTokens = [];

        #endregion
    }

    public class TShockConfig(IPluginConfigRegistrar configRegistrar, string fileNameWithoutExtension) : ServerConfigFile<TShockSettings>(configRegistrar, fileNameWithoutExtension)
    {
        /// <summary>
		/// Dumps all configuration options to a text file in Markdown format
		/// </summary>
		public static void DumpDescriptions() {
            var sb = new StringBuilder();
            var defaults = new TShockSettings();

            foreach (var field in defaults.GetType().GetFields().OrderBy(f => f.Name)) {
                if (field.IsStatic)
                    continue;

                var name = field.Name;
                var type = field.FieldType.Name;

                var descattr =
                    field.GetCustomAttributes(false).FirstOrDefault(o => o is DescriptionAttribute) as DescriptionAttribute;
                var desc = descattr != null && !string.IsNullOrWhiteSpace(descattr.Description) ? descattr.Description : "None";

                var def = field.GetValue(defaults);

                sb.AppendLine($"## {name}  ");
                sb.AppendLine($"{desc}");
                sb.AppendLine(GetString("* **Field type**: `{0}`", type));
                sb.AppendLine(GetString("* **Default**: `{0}`", def));
                sb.AppendLine();
            }

            File.WriteAllText("docs/config-file-descriptions.md", sb.ToString());
        }
    }

    /// <summary>
    /// Constants for valid PvP mode strings used with <see cref="TShockSettings.PvPMode"/>.
    /// </summary>
    public static class PvPModes
    {
        /// <summary>Default mode where players choose whether to enable PvP.</summary>
        public const string Normal = "normal";

        /// <summary>PvP is permanently forced on for all players.</summary>
        public const string Always = "always";

        /// <summary>PvP is forced on, but only for players who are not on a team.</summary>
        public const string PvPWithNoTeam = "pvpwithnoteam";

        /// <summary>PvP is permanently disabled for all players.</summary>
        public const string Disabled = "disabled";
    }
}
