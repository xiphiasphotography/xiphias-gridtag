local LrProgressScope = import 'LrProgressScope'
local LrFunctionContext = import 'LrFunctionContext'
local json = require 'json'
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
require 'Prefs'

local function quote(value)
    return '"' .. tostring(value):gsub('"', '\\"') .. '"'
end

local function requireFile(path, label)
    if path == nil or path == "" or not LrFileUtils.exists(path) then
        error(label .. " ontbreekt. Open XiPHiAS GridTag: instellingen en kies het bestand via Bladeren.")
    end
end

local function photoIndex(photos)
    local result = {}
    for _, photo in ipairs(photos) do result[photo.localIdentifier] = photo end
    return result
end

local function execute(manual, context)
    local prefs = LrPrefs.prefsForPlugin()
    local catalog = LrApplication.activeCatalog()
    local selected = Manifest.getSelectedPhotos(catalog, manual)
    local manifest = Manifest.build(catalog, manual, selected)
    if #manifest.photos == 0 then
        LrDialogs.message("XiPHiAS GridTag", manual and "Geen foto's met een handmatig nummer gevonden." or "Geen Picks gevonden in de selectie of huidige weergave.")
        return
    end

    requireFile(prefs.gridtagPath, "gridtag.exe")
    requireFile(prefs.entryListPath, "entrylist.csv")
    requireFile(prefs.sessionPath, "session.json")

    local recognitionArguments = ""
    if not manual then
        local root = prefs.recognitionRoot
        if not root or root == "" then root = LrPathUtils.parent(LrPathUtils.parent(_PLUGIN.path)) end
        local detectorConfig = LrPathUtils.child(root, "samples/detector.yolox.json")
        local carDetector = LrPathUtils.child(root, "Models/car-detection/yolox_s.onnx")
        local textDetector = LrPathUtils.child(root, "Models/driver-name/PP-OCRv6_det_small.onnx")
        local textRecognizer = LrPathUtils.child(root, "Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx")
        local dictionary = LrPathUtils.child(root, "Models/driver-name/ppocrv5_latin_dict.txt")
        for _, path in ipairs({ detectorConfig, carDetector, textDetector, textRecognizer, dictionary }) do
            requireFile(path, "Herkenningsbestand " .. path .. " (controleer de herkenningsmap)")
        end
        recognitionArguments = " --vision-config " .. quote(detectorConfig) ..
            " --detector-provider cpu --number-ocr " .. quote(textDetector) ..
            " --number-ocr-model " .. quote(textRecognizer) ..
            " --number-ocr-dictionary " .. quote(dictionary)
    end

    local work = LrPathUtils.child(LrPathUtils.getStandardFilePath("temp"), "GridTag-" .. tostring(os.time()))
    LrFileUtils.createDirectory(work)
    local manifestPath = LrPathUtils.child(work, "manifest.json")
    local resultsPath = LrPathUtils.child(work, "results.json")
    local file = assert(io.open(manifestPath, "wb"))
    file:write(Manifest.encode(manifest))
    file:close()

    local cancelPath = LrPathUtils.child(work, "cancel.request")
    local progressPath = LrPathUtils.child(work, "progress.json")
    local debugPath = LrPathUtils.child(work, "recognition-debug.jsonl")
    local command = quote(prefs.gridtagPath) .. " run" ..
        " --manifest " .. quote(manifestPath) ..
        " --entrylist " .. quote(prefs.entryListPath) ..
        " --session " .. quote(prefs.sessionPath) ..
        " --out " .. quote(resultsPath) .. " --debug-log " .. quote(debugPath) .. " --progress-file " .. quote(progressPath) .. " --cancel-file " .. quote(cancelPath) .. recognitionArguments
    local logPath = LrPathUtils.child(work, 'cli.log')
    command = command .. ' > ' .. quote(logPath) .. ' 2>&1'
    -- Windows cmd removes the outer quotes when the executable path is quoted.
    if WIN_ENV then command = '"' .. command .. '"' end
    local progress = LrProgressScope {
        title = "XiPHiAS GridTag", caption = "Modellen laden en foto's voorbereiden...",
        functionContext = context,
    }
    progress:setCancelable(true)
    progress:setPortionComplete(0, #manifest.photos)
    local finished = false
    LrTasks.startAsyncTask(function()
        while not finished do
            if progress:isCanceled() then
                local request = io.open(cancelPath, "wb")
                if request then request:write("stop"); request:close() end
                progress:setCaption("Stoppen na de huidige foto...")
            end
            local file = io.open(progressPath, "rb")
            if file then
                local contents = file:read("*a")
                file:close()
                local valid, state = pcall(json.decode, contents)
                if not progress:isCanceled() and valid and type(state) == "table" and type(state.completed) == "number" and type(state.total) == "number" and state.total > 0 then
                    progress:setPortionComplete(state.completed, state.total)
                    if type(state.fileName) == "string" and type(state.current) == "number" then
                        progress:setCaption("Foto " .. state.current .. "/" .. state.total .. " — " .. state.fileName)
                    else
                        progress:setCaption("Herkenning: " .. state.completed .. "/" .. state.total .. " foto's verwerkt")
                    end
                end
            end
            LrTasks.sleep(0.5)
        end
    end)
    local ok, exitCode = LrTasks.pcall(function() return LrTasks.execute(command) end)
    finished = true
    local stopped = progress:isCanceled()
    progress:setCancelable(false)
    if not ok then error("CLI starten mislukt: " .. tostring(exitCode)) end
    if exitCode ~= 0 then
        local logFile = io.open(logPath, "rb")
        local details = logFile and logFile:read(4096) or "Geen foutuitvoer beschikbaar."
        if logFile then logFile:close() end
        error("CLI eindigde met exitcode " .. tostring(exitCode) .. "\n" .. (details or "") .. "\nLogbestand: " .. logPath)
    end

    local pluginLogPath = LrPathUtils.child(work, "plugin-debug.log")
    local function logStage(stage)
        local log = io.open(pluginLogPath, "ab")
        if log then log:write(stage .. "\n"); log:close() end
    end
    logStage("CLI voltooid; resultaten lezen en valideren")
    local results = Results.validateForManifest(Results.read(resultsPath), manifest, stopped or LrFileUtils.exists(cancelPath))
    progress:setCaption("Metadata toepassen in Lightroom...")
    logStage("Catalogusmetadata en trefwoorden toepassen")
    local applied, applyError = LrTasks.pcall(function() Writer.apply(catalog, photoIndex(selected), results) end)
    if not applied then
        logStage("FOUT: " .. tostring(applyError))
        error("Lightroom metadata toepassen: " .. tostring(applyError) .. "\nPlug-inlog: " .. pluginLogPath)
    end
    logStage("Catalogusmetadata toegepast")
    progress:done()
    LrDialogs.message("XiPHiAS GridTag", (stopped and "Gestopt. Afgeronde resultaten: " or "XiPHiAS GridTag heeft ") .. tostring(#results.photos) .. " foto('s) verwerkt.\nHerkenningslog: " .. debugPath)
end

function Run.execute(manual, context)
    local ok, message = LrTasks.pcall(function() execute(manual, context) end)
    if not ok then
        logger:error(tostring(message))
        LrDialogs.message("XiPHiAS GridTag fout", tostring(message), "ok")
    end
end

return Run
