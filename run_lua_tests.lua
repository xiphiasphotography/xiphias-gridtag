-- Entry point for the XiPHiAS GridTag Lua plugin tests.
--
-- Runs the harness in tests/GridTag.Lua.Tests with a plain Lua 5.1 interpreter
-- (Lightroom's Lua version). No Lightroom installation is required.
--
-- Usage from the repository root:  lua run_lua_tests.lua

local testsDirectory = (arg and arg[0] or "run_lua_tests.lua"):match("^(.*)[/\\][^/\\]*$") or "."
local bootstrap = assert(loadfile(testsDirectory .. "/tests/GridTag.Lua.Tests/test_runner.lua"))
os.exit(bootstrap() == 0 and 0 or 1)
