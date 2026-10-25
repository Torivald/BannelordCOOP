using System;
using System.IO;
using BannerCoopTW.Core;
using BannerCoopTW.Core.Config;
using BannerCoopTW.Core.Logging;
using BannerCoopTW.Spikes;
using BannerCoopTW.Spikes.A1;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace BannerCoopTW
{
    /// <summary>Module entry point (SubModule.xml: <c>BannerCoopTW.SubModule</c>).</summary>
    public class SubModule : MBSubModuleBase
    {
        private static FileLog? _log;
        private static ModConfig _config = new ModConfig();
        private bool _announced;
        private float _menuTime = -1f;
        private bool _autoLoaded;

        public static ILog? Log => _log;

        public static bool SpikesEnabled => _config.SpikesEnabled;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
                _config = ModConfig.LoadOrCreate(Path.Combine(docs, "Configs", ModInfo.Id, "config.ini"));

                _log = new FileLog(Path.Combine(docs, "Logs", ModInfo.Id), ModInfo.Id)
                {
                    MinLevel = _config.LogLevel,
                    Mirror = (level, message) => Debug.Print($"[{ModInfo.Id}] {level}: {message}"),
                };
                _log.Info($"{ModInfo.Id} v{ModInfo.Version} loading (OnSubModuleLoad), log file: {_log.FilePath}");
                foreach (var problem in _config.Problems)
                    _log.Warning("config: " + problem);
            }
            catch (Exception e)
            {
                Debug.Print($"[{ModInfo.Id}] failed to initialise logging: {e}");
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_announced)
                return;
            _announced = true;
            _menuTime = 0f;
            try
            {
                _log?.Info("main menu reached (OnBeforeInitialModuleScreenSetAsRoot)");
                if (_config.ShowLoadedMessage)
                    InformationManager.DisplayMessage(new InformationMessage($"{ModInfo.Id} v{ModInfo.Version} loaded"));
            }
            catch (Exception e)
            {
                _log?.Error("OnBeforeInitialModuleScreenSetAsRoot failed", e);
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            if (_config.SpikesEnabled && SpikeRunner.Busy)
            {
                try
                {
                    SpikeRunner.Tick();
                }
                catch (Exception e)
                {
                    _log?.Error("spike runner failed", e);
                }
            }

            if (_config.SpikesEnabled && _config.SpikeL1.Length > 0 && _menuTime >= 0f)
            {
                _menuTime += dt;
                try
                {
                    Spikes.L1.L1Driver.Tick(dt, _menuTime, TryBecomeSpikeHost);
                }
                catch (Exception e)
                {
                    _log?.Error("L1 driver failed", e);
                }

                return;
            }

            if (_config.SpikesEnabled && _config.SpikeA1.Length > 0 && _menuTime >= 0f)
            {
                _menuTime += dt;
                try
                {
                    A1Driver.Mode = _config.SpikeA1.Contains(":") ? _config.SpikeA1.Substring(_config.SpikeA1.IndexOf(':') + 1) : string.Empty;
                    A1Driver.Tick(dt, _menuTime, TryBecomeSpikeHost);
                }
                catch (Exception e)
                {
                    _log?.Error("A1 driver failed", e);
                }

                return;
            }

            if (_autoLoaded || _menuTime < 0f || !_config.SpikesEnabled || string.IsNullOrEmpty(_config.SpikeAutoLoadSave))
                return;
            _menuTime += dt;
            if (_menuTime < 8f)
                return;
            _autoLoaded = true;
            _log?.Info(SpikeS1.LoadSave(_config.SpikeAutoLoadSave));
        }

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            if (!_config.SpikesEnabled)
                return;
            try
            {
                LoadTimer.Finish();
                SpikeRunner.Enqueue(_config.SpikeAutoRun);
                if (!string.IsNullOrEmpty(_config.SpikeAutoRunHost) || !string.IsNullOrEmpty(_config.SpikeAutoRunClient))
                {
                    var host = TryBecomeSpikeHost();
                    _log?.Info("spike role: " + (host ? "host" : "client"));
                    SpikeRunner.Enqueue(host ? _config.SpikeAutoRunHost : _config.SpikeAutoRunClient);
                }
            }
            catch (Exception e)
            {
                _log?.Error("spike auto-run failed", e);
            }
        }

        private static System.IO.FileStream? _roleLock;

        /// <summary>First instance to take an exclusive lock on the role file is the spike host; the lock is held until the process exits.</summary>
        private static bool TryBecomeSpikeHost()
        {
            try
            {
                var path = System.IO.Path.Combine(SpikeS1.OutDir, "role-host.lock");
                _roleLock = new System.IO.FileStream(path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
                return true;
            }
            catch (System.IO.IOException)
            {
                return false;
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            try
            {
                _log?.Info("unloading (OnSubModuleUnloaded)");
                _log?.Dispose();
                _log = null;
            }
            catch
            {
                // Never throw from shutdown.
            }

            base.OnSubModuleUnloaded();
        }
    }
}
