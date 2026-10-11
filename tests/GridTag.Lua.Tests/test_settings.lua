_PLUGIN = { path = 'root/lightroom/plugin' }
local prefs = {}
local answer = 'ok'
local factory = {}
for _, name in ipairs({ 'column', 'row', 'static_text', 'edit_field', 'push_button' }) do factory[name] = function(self, value) return value end end
factory.control_spacing = function() return 5 end
function import(name)
    if name == 'LrPathUtils' then return { parent = function(path) return path:match('^(.*)/[^/]+$') end } end
    if name == 'LrPrefs' then return { prefsForPlugin = function() return prefs end } end
    if name == 'LrBinding' then return { makePropertyTable = function(context) assert(context); return {} end } end
    if name == 'LrView' then return { osFactory = function() return factory end, bind = function(value) return value end } end
    if name == 'LrTasks' then return { pcall = pcall } end
    if name == 'LrFunctionContext' then return { postAsyncTaskWithContext = function(name, action) action({}) end } end
    if name == 'LrDialogs' then return {
        message = function(title, message) error(message) end,
        presentModalDialog = function(args)
            assert(args.contents.bind_to_object == args.propertyTable)
            args.propertyTable.gridtagPath = 'S:/GridTag/gridtag.exe'
            args.propertyTable.entryListPath = 'S:/event/entrylist.csv'
            args.propertyTable.sessionPath = 'S:/event/session.json'
            return answer
        end,
    } end
    error(name)
end
package.path = 'lightroom/XiPHiAS.GridTag.lrdevplugin/?.lua;' .. package.path
assert(loadfile('lightroom/XiPHiAS.GridTag.lrdevplugin/Settings.lua'))()
assert(prefs.gridtagPath == 'S:/GridTag/gridtag.exe')
assert(prefs.entryListPath == 'S:/event/entrylist.csv' and prefs.sessionPath == 'S:/event/session.json')
assert(prefs.chunkSize == 50)
prefs.gridtagPath = 'keep.exe'
answer = 'cancel'
assert(loadfile('lightroom/XiPHiAS.GridTag.lrdevplugin/Settings.lua'))()
assert(prefs.gridtagPath == 'keep.exe')
io.write('Settings save/cancel checks passed\n')
assert(prefs.recognitionRoot == 'root')
