Runnable samples live here.

| Folder | What it is |
| --- | --- |
| `Jev.Sdk.Sample/` | A minimal console client. Pick an endpoint, send a prompt, print the answer. Also holds the `appSettings.json` and `.env.example` both samples use. |
| `Game/` | Escape the Room: a small demo game built on the client, showing `Choice`, `Score` and `Noul` answering five questions in one call. See `Game/README.md`. |

Both resolve their API key the same way: a command-line argument, then `TYPESAFE_API_KEY`, then the
settings files. Neither needs a file to run — with nothing configured they prompt for a key.
