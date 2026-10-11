package.path = 'lightroom/XiPHiAS.GridTag.lrdevplugin/?.lua;' .. package.path
local Collections = require 'Collections'
local committed, pending, inGate = {}, {}, false
local creates = 0
local function item(name)
    local members = {}
    return { getName = function() return name end,
        addPhotos = function(self, photos) assert(inGate); for _, photo in ipairs(photos) do members[photo] = true end end,
        removePhotos = function(self, photos) assert(inGate); for _, photo in ipairs(photos) do members[photo] = nil end end,
        members = members }
end
local catalog = {}
function catalog:getChildCollections() return committed end
function catalog:createCollection(name) assert(inGate); creates = creates + 1; pending[#pending + 1] = item(name); return nil end
function catalog:withWriteAccessDo(name, action)
    assert(not inGate); inGate = true; action(); inGate = false
    for _, collection in ipairs(pending) do committed[#committed + 1] = collection end
    pending = {}
end
local prepared = Collections.prepare(catalog)
local photo = {}
catalog:withWriteAccessDo('apply', function() Collections.apply(catalog, photo, 'noCar', prepared) end)
assert(prepared.noCar.members[photo] and not prepared.review.members[photo])
catalog:withWriteAccessDo('apply', function() Collections.apply(catalog, photo, 'review', prepared) end)
assert(prepared.review.members[photo] and not prepared.noCar.members[photo])
Collections.prepare(catalog)
assert(creates == 2)
io.write('Collections commit and rerun checks passed\n')