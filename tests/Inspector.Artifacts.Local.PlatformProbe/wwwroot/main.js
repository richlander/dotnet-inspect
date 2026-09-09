import { dotnet } from './_framework/dotnet.js'

const runtime = await dotnet.create()
const exitCode = await runtime.runMain()
process.exitCode = exitCode
