local LrPathUtils = import 'LrPathUtils'
local LrBinding = import 'LrBinding'
local LrDialogs = import 'LrDialogs'
local LrFunctionContext = import 'LrFunctionContext'
local LrPrefs = import 'LrPrefs'
local LrTasks = import 'LrTasks'
local LrView = import 'LrView'

local function pathRow(f, prefs, title, key, fileTypes, directory)
    return f:row {
        f:static_text { title = title },
        f:edit_field { value = LrView.bind { key = key }, width_in_chars = 55 },
        f:push_button {
            title = "Bladeren...",
            action = function()
                local selected = LrDialogs.runOpenPanel {
                    title = title,
                    prompt = "Selecteren",
                    canChooseFiles = not directory,
                    canChooseDirectories = directory or false,
                    allowsMultipleSelection = false,
                    fileTypes = fileTypes,
                }
                if selected and selected[1] then prefs[key] = selected[1] end
            end,
        },
    }
end

require 'Prefs'
local settingKeys = { 'gridtagPath', 'entryListPath', 'sessionPath', 'chunkSize', 'personSeparator', 'recognitionRoot' }

local function showSettings(context)
    local prefs = LrPrefs.prefsForPlugin()
    local properties = LrBinding.makePropertyTable(context)
    for _, key in ipairs(settingKeys) do properties[key] = prefs[key] end
    if not properties.recognitionRoot or properties.recognitionRoot == '' then
        properties.recognitionRoot = LrPathUtils.parent(LrPathUtils.parent(_PLUGIN.path))
    end
    local f = LrView.osFactory()
    local contents = f:column {
        bind_to_object = properties,
        spacing = f:control_spacing(),
        pathRow(f, properties, "Pad naar gridtag.exe", "gridtagPath", { "exe" }),
        pathRow(f, properties, "Pad naar entrylist.csv", "entryListPath", { "csv" }),
        pathRow(f, properties, "Pad naar session.json", "sessionPath", { "json" }),
        pathRow(f, properties, "Herkenningsmap (Models + samples)", "recognitionRoot", nil, true),
        f:row { f:static_text { title = "Chunkgrootte" }, f:edit_field { value = LrView.bind { key = "chunkSize" }, width_in_chars = 8 } },
        f:row { f:static_text { title = "Scheidingsteken voor rijders" }, f:edit_field { value = LrView.bind { key = "personSeparator" } } },
    }
    local result = LrDialogs.presentModalDialog {
        title = "XiPHiAS GridTag instellingen",
        contents = contents,
        actionVerb = "Opslaan",
        propertyTable = properties,
    }
    if result == 'ok' then
        for _, key in ipairs(settingKeys) do prefs[key] = properties[key] end
    end
end

LrFunctionContext.postAsyncTaskWithContext("GridTag instellingen", function(context)
    local ok, message = LrTasks.pcall(function() showSettings(context) end)
    if not ok then LrDialogs.message("XiPHiAS GridTag fout", tostring(message), "ok") end
end)
