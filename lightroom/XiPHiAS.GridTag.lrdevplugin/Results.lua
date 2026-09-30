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

function Results.validateForManifest(results, manifest)
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
