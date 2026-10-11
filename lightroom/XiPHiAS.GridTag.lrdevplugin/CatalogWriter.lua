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

local function applyKeywords(photo, catalog, names, keywords)
    for _, oldName in ipairs(oldKeywords(photo)) do
        local keyword = assert(keywords[oldName], "Ontbrekend trefwoord: " .. oldName)
        photo:removeKeyword(keyword)
    end
    for _, name in ipairs(names or {}) do
        local keyword = assert(keywords[name], "Ontbrekend trefwoord: " .. name)
        photo:addKeyword(keyword)
    end
    photo:setPropertyForPlugin(_PLUGIN, "keywords", json.encode(names or {}))
end

local function applyPhoto(catalog, photo, result, toolVersion, collections, keywords)
    if result.status ~= "manual" and photo:getPropertyForPlugin(_PLUGIN, "status") == "manual" then
        return
    end
    if result.status == "auto" or result.status == "manual" then
        local fields = result.fields or {}
        setRaw(photo, "headline", fields.headline)
        if not setRaw(photo, "title", fields.headline) then
            error("Title kon niet worden ingevuld voor foto " .. tostring(photo.localIdentifier))
        end
        if not setRaw(photo, "copyName", fields.headline) then
            error("Copy Name kon niet worden ingevuld voor foto " .. tostring(photo.localIdentifier))
        end
        setRaw(photo, "caption", fields.caption)
        setRaw(photo, "altTextAccessibility", fields.altText)
        setRaw(photo, "extDescrAccessibility", fields.extDescription)
        local separator = LrPrefs.prefsForPlugin().personSeparator or ", "
        setRaw(photo, "personShown", table.concat(fields.persons or {}, separator))
        applyKeywords(photo, catalog, fields.keywords, keywords)
    end

    local primary = result.cars and result.cars[1]
    photo:setPropertyForPlugin(_PLUGIN, "status", result.status)
    photo:setPropertyForPlugin(_PLUGIN, "number", primary and primary.number or nil)
    photo:setPropertyForPlugin(_PLUGIN, "confidence", primary and primary.confidence or nil)
    photo:setPropertyForPlugin(_PLUGIN, "reasons", table.concat(result.reasons or {}, ", "))
    photo:setPropertyForPlugin(_PLUGIN, "session", result.session)
    photo:setPropertyForPlugin(_PLUGIN, "toolVersion", toolVersion)
    Collections.apply(catalog, photo, result.status, collections)
end

function Writer.apply(catalog, photoById, results)
    local prefs = LrPrefs.prefsForPlugin()
    local chunkSize = tonumber(prefs.chunkSize) or 50
    local collections = Collections.prepare(catalog)
    local keywords = {}
    local names = {}
    for _, result in ipairs(results.photos) do
        local photo = photoById[result.id]
        if photo and (result.status == "auto" or result.status == "manual") and
            (result.status == "manual" or photo:getPropertyForPlugin(_PLUGIN, "status") ~= "manual") then
            for _, name in ipairs(oldKeywords(photo)) do names[name] = true end
            for _, name in ipairs((result.fields or {}).keywords or {}) do names[name] = true end
        end
    end
    if next(names) then
        catalog:withWriteAccessDo("GridTag trefwoorden voorbereiden", function()
            for name in pairs(names) do
                keywords[name] = catalog:createKeyword(name, {}, true, nil, true)
            end
        end)
    end
    for first = 1, #results.photos, chunkSize do
        local last = math.min(first + chunkSize - 1, #results.photos)
        catalog:withWriteAccessDo("GridTag metadata", function()
            for index = first, last do
                local result = results.photos[index]
                local photo = photoById[result.id]
                if photo then applyPhoto(catalog, photo, result, results.toolVersion, collections, keywords) end
            end
        end)
    end
end

return Writer
