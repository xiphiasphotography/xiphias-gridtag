local LrFunctionContext = import 'LrFunctionContext'
local LrTasks = import 'LrTasks'
local Run = require 'Run'

LrFunctionContext.postAsyncTaskWithContext("GridTag manual", function()
    Run.execute(true)
end)
