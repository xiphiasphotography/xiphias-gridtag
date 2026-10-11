package.path = 'lightroom/XiPHiAS.GridTag.lrdevplugin/?.lua;' .. package.path
local Manifest = require 'Manifest'
local function photo(id, pick)
    return { localIdentifier = id, getPropertyForPlugin = function() return nil end, getRawMetadata = function(self, key)
        return ({ pickStatus = pick, uuid = 'u' .. id, path = 'p' .. id, dateTimeOriginalISO8601 = '2026-09-18T14:00:00' })[key]
    end }
end
local a, b = photo(1, 1), photo(2, 0)
local catalog = { getTargetPhoto = function() return a end, getTargetPhotos = function() return { a, b } end }
assert(#Manifest.build(catalog, false).photos == 1)
assert(Manifest.build(catalog, false).photos[1].id == 1)
catalog.getTargetPhotos = function() return { a } end
assert(#Manifest.build(catalog, false).photos == 1)
catalog.getTargetPhoto = function() return nil end
catalog.getTargetPhotos = function() return { a, b } end
assert(#Manifest.build(catalog, false).photos == 1)
assert(#Manifest.build(catalog, true).photos == 0)
io.write('Selection regression checks passed\n')