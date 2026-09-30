local LrFunctionContext = import 'LrFunctionContext'
local LrTasks = import 'LrTasks'
local Run = require 'Run'

LrFunctionContext.postAsyncTaskWithContext("GridTag", function()
    Run.execute(false)
end)
