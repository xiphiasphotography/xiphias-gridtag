local LrPrefs = import 'LrPrefs'
local LrLogger = import 'LrLogger'
local json = require 'json'
local Collections = require 'Collections'

local logger = LrLogger('GridTag')
local Writer = {}

local function setRaw(photo, key, value)
    local ok, message = pcall(function() photo:setRawMetadata(key, value) end)
    if not ok then logger:error("photo " .. tostring(photo.localIdentifier) .. " field " .. key .. ": " .. tostring(message)) end
    return ok
end

local function oldKeywords(photo)
    local value = photo:getPropertyForPlugin(_PLUGIN, "keywords")
    if not value or value == "" then return {} end
    local ok, decoded = pcall(json.decode, value)
    return ok and decoded or {}
end

local function applyKeywords(photo, catalog, names)
    for _, oldName in ipairs(oldKeywords(photo)) do
        local keyword = catalog:createKeyword(oldName, {}, true, nil, true)
        photo:removeKeyword(keyword)
    end
    for _, name in ipairs(names or {}) do
        local keyword = catalog:createKeyword(name, {}, true, nil, true)
        photo:addKeyword(keyword)
    end
    photo:setPropertyForPlugin(_PLUGIN, "keywords", json.encode(names or {}))
end

local function applyPhoto(catalog, photo, result, toolVersion)
    if result.status ~= "manual" and photo:getRawMetadata("status") == "manual" then
        return
    end
    if result.status == "auto" or result.status == "manual" then
        local fields = result.fields or {}
        setRaw(photo, "headline", fields.headline)
        setRaw(photo, "caption", fields.caption)
        setRaw(photo, "altTextAccessibility", fields.altText)
        setRaw(photo, "extDescrAccessibility", fields.extDescription)
        local separator = LrPrefs.prefsForPlugin().personSeparator or ", "
        setRaw(photo, "personShown", table.concat(fields.persons or {}, separator))
        applyKeywords(photo, catalog, fields.keywords)
    end

    local primary = result.cars and result.cars[1]
    setRaw(photo, "status", result.status)
    setRaw(photo, "number", primary and primary.number or nil)
    setRaw(photo, "confidence", primary and primary.confidence or nil)
    setRaw(photo, "reasons", table.concat(result.reasons or {}, ", "))
    setRaw(photo, "session", result.session)
    setRaw(photo, "toolVersion", toolVersion)
    Collections.apply(catalog, photo, result.status)
end

function Writer.apply(catalog, photoById, results)
    local prefs = LrPrefs.prefsForPlugin()
    local chunkSize = tonumber(prefs.chunkSize) or 50
    for first = 1, #results.photos, chunkSize do
        local last = math.min(first + chunkSize - 1, #results.photos)
        catalog:withWriteAccessDo("GridTag metadata", function()
            for index = first, last do
                local result = results.photos[index]
                local photo = photoById[result.id]
                if photo then applyPhoto(catalog, photo, result, results.toolVersion) end
            end
        end)
    end
end

return Writer
