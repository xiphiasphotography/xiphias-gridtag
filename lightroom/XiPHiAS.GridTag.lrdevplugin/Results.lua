local json = require 'json'

local Results = {}

function Results.read(path)
    local file = assert(io.open(path, "rb"), "Kan results.json niet lezen")
    local text = file:read("*a")
    file:close()
    local result = json.decode(text)
    if type(result) ~= "table" or result.schemaVersion ~= 1 or type(result.photos) ~= "table" then
        error("Ongeldige results.json schemaVersion")
    end

    local seen = {}
    for _, photo in ipairs(result.photos) do
        if photo.id == nil or seen[photo.id] then error("Dubbel of ontbrekend photo id in results.json") end
        seen[photo.id] = true
        if photo.status == nil then error("Ontbrekende status in results.json") end
    end
    return result
end

function Results.validateForManifest(results, manifest, stopped)
    if stopped then
        if #results.photos > #manifest.photos then error("Te veel resultaten na stoppen") end
        for index, photo in ipairs(results.photos) do
            if photo.id ~= manifest.photos[index].id then error("Onverwachte resultaatvolgorde na stoppen") end
        end
        local completed = {}
        for index = 1, #results.photos do completed[index] = manifest.photos[index] end
        manifest = { photos = completed }
    end
    local expected = {}
    for _, photo in ipairs(manifest.photos) do expected[photo.id] = true end
    local actual = {}
    for _, photo in ipairs(results.photos) do
        if not expected[photo.id] then error("Onbekend photo id in results.json: " .. tostring(photo.id)) end
        actual[photo.id] = true
    end
    for id in pairs(expected) do
        if not actual[id] then error("Ontbrekend photo id in results.json: " .. tostring(id)) end
    end
    return results
end

return Results
