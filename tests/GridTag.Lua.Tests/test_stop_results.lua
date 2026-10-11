package.path = 'lightroom/XiPHiAS.GridTag.lrdevplugin/?.lua;' .. package.path
local Results = require 'Results'
local manifest = { photos = { { id = 1 }, { id = 2 }, { id = 3 } } }
local partial = { photos = { { id = 1 }, { id = 2 } } }
assert(Results.validateForManifest(partial, manifest, true) == partial)
assert(not pcall(Results.validateForManifest, partial, manifest, false))
assert(not pcall(Results.validateForManifest, { photos = { { id = 2 } } }, manifest, true))
assert(Results.validateForManifest({ photos = {} }, manifest, true))
io.write('Stopped batch prefix validation checks passed\n')