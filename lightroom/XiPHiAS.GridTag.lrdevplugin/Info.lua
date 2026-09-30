return {
    LrSdkVersion = 6.0,
    LrSdkMinimumVersion = 6.0,
    LrToolkitIdentifier = "net.xiphias.gridtag",
    LrPluginName = "GridTag",
    LrLibraryMenuItems = {
        {
            title = "GridTag: tag Picks",
            file = "Runner.lua",
            enabledWhen = "photosSelected",
        },
        {
            title = "GridTag: verwerk handmatige nummers",
            file = "ManualRunner.lua",
            enabledWhen = "photosSelected",
        },
        {
            title = "GridTag: instellingen...",
            file = "Settings.lua",
        },
    },
    LrMetadataProvider = "Metadata.lua",
    LrMetadataTagset = "Tagset.lua",
}
