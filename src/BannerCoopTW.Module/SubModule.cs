using System;
using System.IO;
using BannerCoopTW.Core;
using BannerCoopTW.Core.Config;
using BannerCoopTW.Core.Logging;
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

        public static ILog? Log => _log;

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
