local LrApplication = import 'LrApplication'
local LrDialogs = import 'LrDialogs'
local LrFileUtils = import 'LrFileUtils'
local LrLogger = import 'LrLogger'
local LrPathUtils = import 'LrPathUtils'
local LrPrefs = import 'LrPrefs'
local LrTasks = import 'LrTasks'
local Manifest = require 'Manifest'
local Results = require 'Results'
local Writer = require 'CatalogWriter'

local logger = LrLogger('GridTag')
local Run = {}

local function quote(value)
    return '"' .. tostring(value):gsub('"', '\\"') .. '"'
end

local function requireFile(path, label)
    if path == nil or path == "" or not LrFileUtils.exists(path) then
        error(label .. " ontbreekt: " .. tostring(path))
    end
end

local function photoIndex(photos)
    local result = {}
    for _, photo in ipairs(photos) do result[photo.localIdentifier] = photo end
    return result
end

local function execute(manual)
    local prefs = LrPrefs.prefsForPlugin()
    local catalog = LrApplication.activeCatalog()
    local selected = catalog:getMultipleSelectedPhotos() or {}
    local manifest = Manifest.build(catalog, manual)
    if #manifest.photos == 0 then
        LrDialogs.message("GridTag", manual and "Geen foto's met een handmatig nummer gevonden." or "Geen Picks geselecteerd.")
        return
    end

    requireFile(prefs.gridtagPath, "gridtag.exe")
    requireFile(prefs.entryListPath, "entrylist.csv")
    requireFile(prefs.sessionPath, "session.json")

    local work = LrPathUtils.child(LrPathUtils.getTempDirectory(), "GridTag-" .. tostring(os.time()))
    LrFileUtils.createDirectory(work)
    local manifestPath = LrPathUtils.child(work, "manifest.json")
    local resultsPath = LrPathUtils.child(work, "results.json")
    local file = assert(io.open(manifestPath, "wb"))
    file:write(Manifest.encode(manifest))
    file:close()

    local command = quote(prefs.gridtagPath) .. " run" ..
        " --manifest " .. quote(manifestPath) ..
        " --entrylist " .. quote(prefs.entryListPath) ..
        " --session " .. quote(prefs.sessionPath) ..
        " --out " .. quote(resultsPath)
    local ok, exitCode = LrTasks.pcall(function() return LrTasks.execute(command) end)
    if not ok then error("CLI starten mislukt: " .. tostring(exitCode)) end
    if exitCode ~= 0 then error("CLI eindigde met exitcode " .. tostring(exitCode)) end

    local results = Results.validateForManifest(Results.read(resultsPath), manifest)
    Writer.apply(catalog, photoIndex(selected), results)
    LrDialogs.message("GridTag", "GridTag heeft " .. tostring(#results.photos) .. " foto('s) verwerkt.")
end

function Run.execute(manual)
    local ok, message = LrTasks.pcall(function() execute(manual) end)
    if not ok then
        logger:error(tostring(message))
        LrDialogs.message("GridTag fout", tostring(message), "ok")
    end
end

return Run
