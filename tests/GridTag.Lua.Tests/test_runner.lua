#!/usr/bin/env lua
-- Minimal test harness for the XiPHiAS GridTag Lightroom plugin modules.
-- Runs with plain Lua 5.1 and needs no Lightroom installation: every Lr* API
-- the modules touch is stubbed below.
--
-- Usage: lua tests/GridTag.Lua.Tests/test_runner.lua
--        (or from the repository root: lua run_lua_tests.lua)

local failures = 0
local checks = 0

local function fail(message)
    failures = failures + 1
    io.write("FAIL: ", message, "\n")
end

local function check(condition, message)
    checks = checks + 1
    if not condition then
        fail(message)
    end
end

local function checkEqual(actual, expected, message)
    checks = checks + 1
    if actual ~= expected then
        fail(string.format("%s (expected %s, got %s)", message, tostring(expected), tostring(actual)))
    end
end

--- Locates the plugin directory relative to this script.
local function pluginDirectory()
    local script = arg and arg[0] or "tests/GridTag.Lua.Tests/test_runner.lua"
    local directory = script:match("^(.*)[/\\][^/\\]*$") or "."
    return directory .. "/../../lightroom/XiPHiAS.GridTag.lrdevplugin"
end

local pluginPath = pluginDirectory()
package.path = pluginPath .. "/?.lua;" .. package.path

--- Shared stub state, reset before every test.
local state

local function resetState()
    state = {
        prefs = { chunkSize = 50, personSeparator = ", " },
        writes = {},          -- every setRawMetadata call as {key, value}
        writeGates = 0,
        keywordAdds = {},
        keywordRemoves = {},
        pluginProperties = {},
        collections = {},
        photoStatus = nil,
        rawStatus = "auto",
        setRawFailures = {},
    }
end

local Lua = {}

--- Builds a catalog stub with collections and keyword creation.
local function makeCatalog()
    local catalog = {}
    function catalog:withWriteAccessDo(name, action)
        state.writeGates = state.writeGates + 1
        state.lastWriteGateName = name
        action()
    end
    function catalog:createKeyword(name, synonyms, includeOnExport, parent, returnExisting)
        state.createdKeywords = state.createdKeywords or {}
        table.insert(state.createdKeywords, name)
        return { name = name }
    end
    function catalog:createCollection(name)
        state.collections[#state.collections + 1] = name
        return {
            name = name,
            addPhotos = function(_, photos) state.collectionAdds = photos end,
            removePhotos = function(_, photos) state.collectionRemoves = photos end,
        }
    end
    return catalog
end

local function makePhoto(id)
    local photo = { localIdentifier = id, id = id }
    function photo:setRawMetadata(key, value)
        if state.setRawFailures[key] then
            error("stub failure for " .. key)
        end
        table.insert(state.writes, { id = id, key = key, value = value })
    end
    function photo:getRawMetadata(key)
        if key == "status" then return state.rawStatus end
        return nil
    end
    function photo:getPropertyForPlugin(_, key)
        return state.pluginProperties[id .. ":" .. key]
    end
    function photo:setPropertyForPlugin(_, key, value)
        state.pluginProperties[id .. ":" .. key] = value
    end
    function photo:addKeyword(keyword)
        table.insert(state.keywordAdds, { id = id, name = keyword.name })
    end
    function photo:removeKeyword(keyword)
        table.insert(state.keywordRemoves, { id = id, name = keyword.name })
    end
    return photo
end

--- Installs the Lr* API stubs used by the plugin modules.
function Lua.install()
    _G._PLUGIN = { id = "net.xiphias.gridtag" }
    _G.WIN_ENV = true

    local imported = {
        LrPrefs = {
            prefsForPlugin = function() return state.prefs end,
        },
        LrLogger = function()
            return { error = function() end, info = function() end, warn = function() end }
        end,
        LrTasks = {
            execute = function() return "exit", "0" end,
            pcall = pcall,
        },
        LrDialogs = {
            message = function() end,
        },
        LrFunctionContext = {
            postAsyncTaskWithContext = function(_, action) action() end,
        },
    }

    _G.import = function(name)
        return imported[name] or error("unexpected Lightroom module import: " .. name)
    end

    resetState()
end

function Lua.state() return state end

local function valuesByKey(id)
    local values = {}
    for _, write in ipairs(state.writes) do
        if write.id == id then
            values[write.key] = write.value
        end
    end
    return values
end

local function countKeywords(list, name)
    local total = 0
    for _, item in ipairs(list) do
        if item.name == name then total = total + 1 end
    end
    return total
end

return function()
    Lua.install()
    local Writer = require("CatalogWriter")

    io.write("CatalogWriter.apply marks status and number\n")
    local catalog = makeCatalog()
    local photo = makePhoto(10)
    Writer.apply(catalog, { [10] = photo }, {
        toolVersion = "0.1.0",
        photos = { id = 10, status = "noCar", reasons = { "no_car_detected" }, session = "FP2" } },
    })
    local values = valuesByKey(10)
    checkEqual(values.status, "noCar", "status should be written")
    checkEqual(values.toolVersion, "0.1.0", "toolVersion should be written")
    checkEqual(values.reasons, "no_car_detected", "reasons should be joined")
    checkEqual(values.number, nil, "a noCar photo must not receive a number")

    io.write("CatalogWriter.apply writes fields and keywords for auto results\n")
    Lua.install()
    Writer = require("CatalogWriter")
    catalog = makeCatalog()
    photo = makePhoto(11)
    Writer.apply(catalog, { [11] = photo }, {
 toolVersion = "0.1.0",
        photos = {
            {
                id = 11,
                status = "auto",
                reasons = {},
                session = "FP2",
                cars = { number = "69", confidence = 0.98, source = "ocr", primary = true } },
                fields = {
                    headline = "#69 Emil Frey Racing",
                    caption = "Caption",
                    altText = "Alt",
                    extDescription = "Ext",
                    keywords = { "Emil Frey Racing", "#69" },
                    persons = { "Thierry Vermeulen", "Ben Green" },
                },
            },
        },
    })
    values = valuesByKey(11)
    checkEqual(values.headline, "#69 Emil Frey Racing", "headline should be written")
    checkEqual(values.personShown, "Thierry Vermeulen, Ben Green", "persons should use the configured separator")
    checkEqual(values.number, "69", "primary number should be written")
    checkEqual(countKeywords(state.keywordAdds, "#69"), 1, "#69 should be added once")
    checkEqual(state.pluginProperties["11:keywords"], '["Emil Frey Racing","#69"]', "applied keywords must be tracked")

    io.write("CatalogWriter.apply clears stale XiPHiAS GridTag keywords before adding new ones\n")
    Lua.install()
    Writer = require("CatalogWriter")
    catalog = makeCatalog()
    photo = makePhoto(12)
    state.pluginProperties["12:keywords"] = '["Old Team","#42"]'
    Writer.apply(catalog, { [12] = photo }, {
        toolVersion = "0.1.0",
        photos = {
            {
                id = 12,
                status = "auto",
                reasons = {},
                cars = { number = "69", confidence = 0.98, source = "ocr", primary = true } },
                fields = { headline = "h", caption = "c", altText = "a", extDescription = "e", keywords = { "Emil Frey Racing" }, persons = {} },
            },
        },
    })
    checkEqual(countKeywords(state.keywordRemoves, "Old Team"), 1, "previous team keyword should be removed")
    checkEqual(countKeywords(state.keywordRemoves, "#42"), 1, "previous number keyword should be removed")
    checkEqual(countKeywords(state.keywordRemoves, "Emil Frey Racing"), 0, "new keywords must not be removed")

    io.write("CatalogWriter.apply never overwrites a manual status\n")
    Lua.install()
    Writer = require("CatalogWriter")
    catalog = makeCatalog()
    photo = makePhoto(13)
    state.rawStatus = "manual"
    Writer.apply(catalog, { [13] = photo }, {
        toolVersion = "0.1.0",
        photos = { id = 13, status = "auto", reasons = {}, cars = {}, fields = { headline = "h", caption = "c", altText = "a", extDescription = "e", keywords = {}, persons = {} } },
    })
    values = valuesByKey(13)
    checkEqual(values.headline, nil, "manual photos must not be overwritten by automatic runs")
    checkEqual(values.status, nil, "manual photos must keep their status")

    io.write("CatalogWriter.apply chunks writes into separate gates\n")
    Lua.install()
    Writer = require("CatalogWriter")
    catalog = makeCatalog()
    state.prefs.chunkSize = 2
    local photos = {}
    local results = { toolVersion = "0.1.0", photos = {} }
    for index = 1, 5 do
        photos[index] = makePhoto(100 + index)
        results.photos[index] = { id = 100 + index, status = "noCar", reasons = {}, session = "FP2" }
    end
    Writer.apply(catalog, photos, results)
    checkEqual(state.writeGates, 3, "five photos with chunkSize 2 should use three write gates")

    io.write("CatalogWriter.apply survives a failing setRawMetadata call\n")
    Lua.install()
    Writer = require("CatalogWriter")
    catalog = makeCatalog()
    photo = makePhoto(14)
    state.setRawFailures["personShown"] = true
    Writer.apply(catalog, { [14] = photo }, {
        toolVersion = "0.1.0",
        photos = {
            {
                id = 14,
                status = "auto",
                reasons = {},
                cars = {},
                fields = { headline = "h", caption = "c", altText = "a", extDescription = "e", keywords = {}, persons = { "One" } },
            },
        },
    })
    values = valuesByKey(14)
    checkEqual(values.headline, "h", "a failing field must not stop the remaining fields")
    checkEqual(values.status, "auto", "status should still be written after a field failure")

    io.write(string.format("\n%d checks, %d failure(s)\n", checks, failures))
    return failures
end
