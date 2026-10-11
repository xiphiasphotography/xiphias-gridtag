local json = require 'json'

local Manifest = {}

local function requireValue(value, name)
    if value == nil or value == "" then error("Ontbrekende manifestwaarde: " .. name) end
    return value
end

function Manifest.getSelectedPhotos(catalog, manual)
    -- Automatic runs use the visible filmstrip when there is no selection.
    if manual and not catalog:getTargetPhoto() then return {} end
    return catalog:getTargetPhotos() or {}
end

local function selectedPhotos(catalog, manual, photos)
    photos = photos or Manifest.getSelectedPhotos(catalog, manual)

    local result = {}
    for _, photo in ipairs(photos) do
        local pickStatus = photo:getRawMetadata("pickStatus")
        local manualNumber = photo:getPropertyForPlugin(_PLUGIN, "manualNumber")
        if (manual and manualNumber and manualNumber ~= "") or (not manual and pickStatus == 1) then
            result[#result + 1] = {
                id = photo.localIdentifier,
                uuid = requireValue(photo:getRawMetadata("uuid"), "uuid"),
                path = requireValue(photo:getRawMetadata("path"), "path"),
                captureTime = requireValue(photo:getRawMetadata("dateTimeOriginalISO8601"), "captureTime"),
                manualNumber = manualNumber,
            }
        end
    end
    return result
end

function Manifest.build(catalog, manual, selected)
    local photos = selectedPhotos(catalog, manual, selected)
    return { schemaVersion = 1, photos = photos }
end

function Manifest.validate(manifest)
    if type(manifest) ~= "table" or manifest.schemaVersion ~= 1 or type(manifest.photos) ~= "table" then
        error("Ongeldige manifest schemaVersion")
    end
    for _, photo in ipairs(manifest.photos) do
        requireValue(photo.id, "id")
        requireValue(photo.uuid, "uuid")
        requireValue(photo.path, "path")
        requireValue(photo.captureTime, "captureTime")
    end
    return manifest
end

function Manifest.encode(manifest)
    Manifest.validate(manifest)
    return json.encode(manifest)
end

return Manifest
