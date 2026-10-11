package.path = 'lightroom/XiPHiAS.GridTag.lrdevplugin/?.lua;' .. package.path
_PLUGIN = { id = 'net.xiphias.gridtag' }
local properties, raw = { manualNumber = '69' }, {}
local standard = { pickStatus = 1, uuid = 'uuid', path = 'photo.CR2', dateTimeOriginalISO8601 = '2026-09-18T14:00:00' }
local photo = { localIdentifier = 1 }
function photo:getRawMetadata(key) assert(standard[key] ~= nil, 'Unknown key: ' .. key); return standard[key] end
function photo:getPropertyForPlugin(plugin, key) assert(plugin == _PLUGIN); return properties[key] end
function photo:setPropertyForPlugin(plugin, key, value) assert(plugin == _PLUGIN); properties[key] = value end
function photo:setRawMetadata(key, value)
    assert(key == 'title' or key == 'copyName' or key == 'headline' or key == 'caption' or key == 'altTextAccessibility' or key == 'extDescrAccessibility' or key == 'personShown', 'Unknown key: ' .. key)
    raw[key] = value
end
local catalog = { getTargetPhoto = function() return photo end, getTargetPhotos = function() return { photo } end }
function catalog:withWriteAccessDo(name, action) action() end
local Manifest = require 'Manifest'
assert(Manifest.build(catalog, true).photos[1].manualNumber == '69')
package.loaded.Collections = { prepare = function() return {} end, apply = function() end }
function import(name)
    if name == 'LrPrefs' then return { prefsForPlugin = function() return { chunkSize = 50 } end } end
    if name == 'LrLogger' then return function() return { error = function(self, message) error(message) end } end end
    error(name)
end
local Writer = require 'CatalogWriter'
Writer.apply(catalog, { [1] = photo }, { toolVersion = 'test', photos = { { id = 1, status = 'review', reasons = { 'low_confidence' }, session = 'FP2' } } })
assert(properties.status == 'review' and properties.reasons == 'low_confidence' and properties.session == 'FP2' and properties.toolVersion == 'test')
properties.status = 'manual'
Writer.apply(catalog, { [1] = photo }, { photos = { { id = 1, status = 'review', reasons = {} } } })
assert(properties.status == 'manual')
io.write('Custom metadata regression checks passed\n')
properties.status = nil
local headline = '#69 Emil Frey Racing Ferrari 296 GT3 EVO'
local result = { toolVersion = 'test', photos = { { id = 1, status = 'auto', reasons = {},
    cars = { { number = '69', confidence = 1 } }, fields = { headline = headline, caption = 'caption',
    altText = 'alt', extDescription = 'ext', persons = {}, keywords = {} } } } }
Writer.apply(catalog, { [1] = photo }, result)
assert(raw.copyName == headline, 'Copy Name must contain the headline export prefix')
assert(raw.title == headline, 'Title must contain the generated headline')
Writer.apply(catalog, { [1] = photo }, result)
assert(raw.copyName == headline, 'Reruns must not append filename or size')
Writer.apply(catalog, { [1] = photo }, { photos = { { id = 1, status = 'review', reasons = {} } } })
assert(raw.copyName == headline, 'Review must preserve existing Copy Name')
properties.status = 'manual'
raw.copyName = 'manual prefix'
Writer.apply(catalog, { [1] = photo }, result)
assert(raw.copyName == 'manual prefix', 'Automatic runs must protect manual Copy Name')
result.photos[1].status = 'manual'
Writer.apply(catalog, { [1] = photo }, result)
assert(raw.copyName == headline, 'Explicit manual processing updates Copy Name')