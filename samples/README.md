Runnable samples live here.

| Folder | What it is |
| --- | --- |
| `Jev.Sdk.Sample/` | A minimal console client. Pick an endpoint, send a prompt, print the answer. Also holds the `appSettings.json` and `.env.example` both samples use. |
| `Game/` | Escape the Room: a small demo game built on the client, showing `Choice`, `Score` and `Noul` answering five questions in one call. See `Game/README.md`. |
| `World/` | A world engine: the same pattern as data. A world is a markdown document (or JSON, or a zip package) declaring its state, questions and rules; nothing about a world is compiled in. Has its own solution. See `World/README.md`. |
| `HomeAssistant/` | A safety-first CLI sample that discovers entities and invokes confirmed, catalog-validated light/switch/fan commands through raw Home Assistant REST calls. See `HomeAssistant/README.md`. |

The Game and World samples resolve their API key the same way: a command-line argument, then
`TYPESAFE_API_KEY`, then the settings files. Neither needs a file to run — with nothing configured
they prompt for a key. The Home Assistant sample prompts for its Jev key and Home Assistant credentials
for each run and does not persist them.
