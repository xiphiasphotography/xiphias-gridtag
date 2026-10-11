package.path = 'lightroom/XiPHiAS.GridTag.lrdevplugin/?.lua;' .. package.path
WIN_ENV = true
local selected = { { localIdentifier = 1 } }
local manifest = { photos = { { id = 1 } } }
local created, launched, applied, closed
local prefs = { recognitionRoot = 'S:/GridTag', gridtagPath = 'gridtag.exe', entryListPath = 'entry.csv', sessionPath = 'session.json' }
package.loaded.Manifest = { getSelectedPhotos = function() return selected end, build = function() return manifest end, encode = function() return '{}' end }
package.loaded.Results = { read = function() return { photos = { { id = 1 } } } end, validateForManifest = function(results) return results end }
package.loaded.CatalogWriter = { apply = function(catalog, photos, results) assert(photos[1] == selected[1]); applied = true end }
function import(name)
    if name == 'LrFunctionContext' then return { callWithContext = function(name, action) action({}) end } end
    if name == 'LrProgressScope' then return function(args)
        assert(args.functionContext)
        return { setCancelable = function(self, value) assert(type(value) == "boolean") end, isCanceled = function() return false end,
            setPortionComplete = function() end, setCaption = function() end, done = function() end }
    end end
    if name == 'LrApplication' then return { activeCatalog = function() return {} end } end
    if name == 'LrPrefs' then return { prefsForPlugin = function() return prefs end } end
    if name == 'LrLogger' then return function() return { error = function(self, message) error(message) end } end end
    if name == 'LrDialogs' then return { showModalProgressDialog = function(args) assert(args.functionContext); return { setPortionComplete = function() end, setCaption = function() end, done = function() end } end, message = function(title, message) assert(title ~= 'XiPHiAS GridTag fout', message) end } end
    if name == 'LrFileUtils' then return { exists = function() return true end, createDirectory = function(path) created = path end } end
    if name == 'LrPathUtils' then return {
        getStandardFilePath = function(which) assert(which == 'temp'); return 'temp' end,
        child = function(parent, child) return parent .. '/' .. child end,
    } end
    if name == 'LrTasks' then return { startAsyncTask = function(action) end, pcall = pcall, execute = function(command) launched = command; return 0 end } end
    error(name)
end
io.open = function(path, mode)
    assert((path == created .. '/manifest.json' and mode == 'wb') or (path == created .. '/plugin-debug.log' and mode == 'ab'))
    return { write = function() end, close = function() closed = true end }
end
require('Run').execute(false, {})
assert(created:match('^temp/GridTag%-'))
assert(launched and applied and closed)
io.write('Run reaches CLI and applies results with SDK temp-path API\n')
assert(launched:sub(1, 2) == '""' and launched:sub(-1) == '"')
assert(launched:find('2>&1', 1, true))
assert(launched:find('--vision-config "S:/GridTag/samples/detector.yolox.json"', 1, true))
assert(launched:find('--detector-provider cpu', 1, true))
assert(launched:find('--number-ocr-model "S:/GridTag/Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx"', 1, true))
assert(launched:find('--number-ocr-dictionary "S:/GridTag/Models/driver-name/ppocrv5_latin_dict.txt"', 1, true))