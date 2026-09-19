# Home Assistant command sample

A deliberately small, safety-first console sample that joins Jev's structured judgement with Home Assistant's REST API.

It does not use or create a Home Assistant SDK. It makes these raw calls:

- `GET /api/states` — discovers the available entities.
- `POST /api/services/{domain}/{service}` — invokes a confirmed command.
- `GET /api/states/{entity_id}` — reads the target again to report the observed final state.

## Run

```sh
dotnet run --project samples/HomeAssistant/Jev.Sdk.HomeAssistant
```

The program prompts for:

1. Home Assistant base URL, for example `http://homeassistant.local:8123`
2. a Home Assistant long-lived access token
3. a Jev API key

Those values remain in process memory only. The sample does not write configuration files, log tokens, or put credentials on the command line.

## Command path

1. The sample discovers `light`, `switch`, and `fan` entities from the live Home Assistant state API.
2. You enter a natural-language command.
3. One Jev request determines whether it is a supported, single-entity command and selects the entity and action from closed sets supplied by the application.
4. A declarative `HomeAssistantCommandFactory` validates the selected entity/action pair against its service catalog. It does not use a domain/action switch block or accept an invented target.
5. The CLI displays the exact service call and sends it only after you type `CONFIRM`.
6. It reads the entity state again and reports whether it reached the expected `on` or `off` state.

The initial catalog is intentionally narrow:

| Domains | Actions | Service calls |
| --- | --- | --- |
| `light`, `switch`, `fan` | `turn_on`, `turn_off` | `{domain}/turn_on`, `{domain}/turn_off` |

It rejects questions, multiple-device/area/group requests, scenes, numeric settings, unsupported entity domains, uncertain selections, and catalog-invalid Jev answers. Extend the catalog through new `HomeAssistantServiceDefinition` entries and focused tests; do not bypass the validation boundary.

## Tests

```sh
dotnet test samples/HomeAssistant/Jev.Sdk.HomeAssistant.Tests
```

The tests verify catalog mapping/rejection and the raw authenticated `GET /api/states` discovery request without requiring a Home Assistant server or API key.
