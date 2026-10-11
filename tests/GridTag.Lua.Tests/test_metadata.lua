-- Lightroom metadata registration regression test; run from the repository root.
local pluginPath = "lightroom/XiPHiAS.GridTag.lrdevplugin"
local checks = 0
local function check(condition, message)
    checks = checks + 1
    assert(condition, message)
end
local function checkEqual(actual, expected, message)
    check(actual == expected, message .. " (got " .. tostring(actual) .. ")")
end
    io.write("Plugin metadata schema matches the Lightroom SDK contract\n")
    local info = assert(loadfile(pluginPath .. "/Info.lua"))()
    local metadata = assert(loadfile(pluginPath .. "/" .. info.LrMetadataProvider))()
    checkEqual(metadata.schemaVersion, 1, "metadata requires an initial schema version")
    check(type(metadata.metadataFieldsForPhotos) == "table", "metadata requires metadataFieldsForPhotos")
    local fieldsById = {}
    for _, field in ipairs(metadata.metadataFieldsForPhotos or {}) do
        check(not fieldsById[field.id], "metadata IDs must be unique")
        fieldsById[field.id] = field
        check(field.dataType == nil or field.dataType == "string" or field.dataType == "enum" or field.dataType == "url", "metadata types must be supported by Lightroom")
        if field.dataType == "enum" then
            for _, option in ipairs(field.values or {}) do
                check(type(option) == "table" and type(option.title) == "string" and type(option.value) == "string", "enum entries require value/title tables")
            end
        end
    end
    local expectedIds = { "status", "number", "manualNumber", "confidence", "reasons", "session", "keywords", "toolVersion" }
    for _, id in ipairs(expectedIds) do
        check(fieldsById[id] ~= nil, "metadata declares " .. id)
    end
    checkEqual(info.LrMetadataTagsetFactory, "Tagset.lua", "tagset uses the SDK factory registration")
    local tagset = assert(loadfile(pluginPath .. "/Tagset.lua"))()
    check(type(tagset.items) == "table", "tagset requires items")
    for index, id in ipairs(expectedIds) do
        checkEqual((tagset.items or {})[index], info.LrToolkitIdentifier .. "." .. id, "tagset uses qualified metadata ID " .. id)
    end

io.write(string.format("%d metadata checks passed\n", checks))