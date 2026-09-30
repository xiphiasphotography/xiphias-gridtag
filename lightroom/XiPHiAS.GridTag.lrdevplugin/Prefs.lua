local LrPrefs = import 'LrPrefs'

local prefs = LrPrefs.prefsForPlugin()

if prefs.chunkSize == nil then prefs.chunkSize = 50 end
if prefs.personSeparator == nil then prefs.personSeparator = ", " end
if prefs.gridtagPath == nil then prefs.gridtagPath = "" end
if prefs.entryListPath == nil then prefs.entryListPath = "" end
if prefs.sessionPath == nil then prefs.sessionPath = "" end
