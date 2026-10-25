using System;
using BannerCoopTW.Core.Logging;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;

namespace BannerCoopTW.Spikes.A1
{
    /// <summary>
    /// Spike A1 (ADR-0006 option A): from the main menu start the same Custom Battle on both instances, then attach <see cref="A1Behavior"/>.
    /// Driven from <c>SubModule.OnApplicationTick</c>; every step is logged, a failure is the result.
    /// </summary>
    public static class A1Driver
    {
        private enum Step
        {
            Idle,
            GameRequested,
            BattleRequested,
            Attached,
        }

        private static Step _step;
        private static float _stepTime;
        private static bool _isHost;

        public static bool IsHost => _isHost;

        /// <summary>Mode string after the colon in <c>SpikeA1=auto:xyz</c> (experiment switches).</summary>
        public static string Mode { get; set; } = string.Empty;

        public static void Tick(float dt, float menuTime, Func<bool> electHost)
        {
            _stepTime += dt;
            switch (_step)
            {
                case Step.Idle:
                    if (menuTime < 10f)
                        return;
                    _isHost = electHost();
                    // The stock option (default on) stops the game when its window loses focus: with two windows on one PC the host froze
                    // for minutes (research 0013). A co-op host must keep running unfocused. Static field only, not saved to the config file.
                    BannerlordConfig.StopGameOnFocusLost = false;
                    SubModule.Log?.Info("A1: role " + (_isHost ? "host" : "client") + "; starting Custom Battle game");
                    MBGameManager.StartNewGame(new CustomGameManager());
                    Advance(Step.GameRequested);
                    break;

                case Step.GameRequested:
                    if (Game.Current != null && Game.Current.GameStateManager.ActiveState is CustomBattleState)
                    {
                        SubModule.Log?.Info("A1: CustomBattleState active, opening the battle mission");
                        StartBattle();
                        Advance(Step.BattleRequested);
                    }
                    else if (_stepTime > 120f)
                    {
                        SubModule.Log?.Info("A1: CustomBattleState not reached in 120 s");
                        Advance(Step.Attached);
                    }

                    break;

                case Step.BattleRequested:
                    var mission = Mission.Current;
                    if (mission != null && mission.CurrentState == Mission.State.Continuing && mission.Agents.Count > 0)
                    {
                        SubModule.Log?.Info("A1: mission Continuing with " + mission.Agents.Count + " agents; attaching A1Behavior");
                        mission.AddMissionBehavior(new A1Behavior(_isHost, Mode));
                        Advance(Step.Attached);
                    }
                    else if (_stepTime > 180f)
                    {
                        SubModule.Log?.Info("A1: mission not Continuing in 180 s");
                        Advance(Step.Attached);
                    }

                    break;
            }

            A1Behavior.Instance?.RunPending();
        }

        private static void Advance(Step next)
        {
            _step = next;
            _stepTime = 0f;
        }

        private static void StartBattle()
        {
            var chars = CustomBattleData.Characters.ToList();
            var objects = Game.Current.ObjectManager;
            var cultureA = objects.GetObject<BasicCultureObject>("empire");
            var cultureB = objects.GetObject<BasicCultureObject>("vlandia");
            var army = 25;
            var m = System.Text.RegularExpressions.Regex.Match(Mode, @"n(\d+)");
            if (m.Success)
                army = int.Parse(m.Groups[1].Value);
            var composition = new CustomBattleCompositionData(0.3f, 0f, 0f);
            var counts = CustomBattleHelper.GetTroopCounts(army, composition);
            var enemyCounts = CustomBattleHelper.GetTroopCounts(army, composition);
            SubModule.Log?.Info("A1: troop counts player=[" + string.Join(",", counts) + "] enemy=[" + string.Join(",", enemyCounts) + "]");
            var parties = CustomBattleHelper.GetCustomBattleParties(chars[0], null, chars[1], cultureA, counts, null, cultureB, enemyCounts, null, true);
            CustomBattleData data;
            if (Mode.Contains("siege"))
            {
                var attackers = new System.Collections.Generic.List<MissionSiegeWeapon>
                {
                    MissionSiegeWeapon.CreateDefaultWeapon(DefaultSiegeEngineTypes.Ladder),
                    MissionSiegeWeapon.CreateDefaultWeapon(DefaultSiegeEngineTypes.Ballista),
                };
                var defenders = new System.Collections.Generic.List<MissionSiegeWeapon> { MissionSiegeWeapon.CreateDefaultWeapon(DefaultSiegeEngineTypes.Ballista) };
                if (Mode.Contains("mach"))
                {
                    // throwing machines (mode 'mach'): the scene decides how many fit, the rest is dropped
                    attackers.Add(MissionSiegeWeapon.CreateDefaultWeapon(DefaultSiegeEngineTypes.Catapult));
                    attackers.Add(MissionSiegeWeapon.CreateDefaultWeapon(DefaultSiegeEngineTypes.Onager));
                    defenders.Add(MissionSiegeWeapon.CreateDefaultWeapon(DefaultSiegeEngineTypes.Trebuchet));
                    defenders.Add(MissionSiegeWeapon.CreateDefaultWeapon(DefaultSiegeEngineTypes.Bricole));
                }

                data = CustomBattleHelper.PrepareBattleData(chars[0], null, parties[0], parties[1], CustomBattlePlayerSide.Attacker, CustomBattlePlayerType.Commander,
                    "Siege", "empire_siege_001", "summer", 12f, attackers, defenders, CustomBattleHelper.GetWallHitpointPercentages(0), 2, false, null);
            }
            else
            {
                data = CustomBattleHelper.PrepareBattleData(chars[0], null, parties[0], parties[1], CustomBattlePlayerSide.Attacker, CustomBattlePlayerType.Commander,
                    "Battle", "battle_terrain_029", "summer", 12f, null, null, null, 0, false, null);
            }

            CustomBattleHelper.StartGame(data);
        }
    }
}
