local LrDialogs = import 'LrDialogs'
local LrFunctionContext = import 'LrFunctionContext'
local LrPrefs = import 'LrPrefs'
local LrTasks = import 'LrTasks'
local LrView = import 'LrView'

local function showSettings()
    local prefs = LrPrefs.prefsForPlugin()
    local f = LrView.osFactory()
    local contents = f:column {
        spacing = f:control_spacing(),
        f:row { f:static_text { title = "Pad naar gridtag.exe" }, f:edit_field { value = LrView.bind { key = "gridtagPath" } } },
        f:row { f:static_text { title = "Pad naar entrylist.csv" }, f:edit_field { value = LrView.bind { key = "entryListPath" } } },
        f:row { f:static_text { title = "Pad naar session.json" }, f:edit_field { value = LrView.bind { key = "sessionPath" } } },
        f:row { f:static_text { title = "Chunkgrootte" }, f:edit_field { value = LrView.bind { key = "chunkSize" }, width_in_chars = 8 } },
        f:row { f:static_text { title = "Scheidingsteken voor rijders" }, f:edit_field { value = LrView.bind { key = "personSeparator" } } },
    }
    LrDialogs.presentModalDialog {
        title = "GridTag instellingen",
        contents = contents,
        actionVerb = "Opslaan",
        propertyTable = prefs,
    }
end

LrFunctionContext.postAsyncTaskWithContext("GridTag instellingen", function()
    local ok, message = LrTasks.pcall(showSettings)
    if not ok then LrDialogs.message("GridTag fout", tostring(message), "ok") end
end)
