return {
    LrSdkVersion = 6.0,
    LrSdkMinimumVersion = 6.0,
    LrToolkitIdentifier = "net.xiphias.gridtag",
    LrPluginName = "XiPHiAS GridTag",
    LrLibraryMenuItems = {
        {
            title = "XiPHiAS GridTag: tag Picks",
            file = "Runner.lua",

        },
        {
            title = "XiPHiAS GridTag: verwerk handmatige nummers",
            file = "ManualRunner.lua",
            enabledWhen = "photosSelected",
        },
        {
            title = "XiPHiAS GridTag: instellingen...",
            file = "Settings.lua",
        },
    },
    LrMetadataProvider = "Metadata.lua",
    LrMetadataTagsetFactory = "Tagset.lua",
}
