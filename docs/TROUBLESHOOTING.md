# bioMass troubleshooting

## Unity 6.6: Input System compile errors mentioning `GetInstanceID()` / `GetEntityId()`

If the Console reports CS0619 errors from `Library/PackageCache/com.unity.inputsystem...`, the project has resolved an Input System package that predates Unity 6000.6's EntityId migration.

bioMass targets Unity 6000.6.3f1 and pins:

```json
"com.unity.inputsystem": "1.20.0"
```

in `Packages/manifest.json`.

After changing package versions, let Unity finish package resolution and script compilation. If Unity keeps using an old cached package after the manifest has changed, close the editor, remove the project's `Library/PackageCache` folder (or the whole generated `Library` folder), and reopen the project so Unity can resolve packages again. Do not edit files inside `Library/PackageCache` as a permanent fix.

## Legacy Input Manager warning

bioMass runtime code uses `UnityEngine.InputSystem`, not the legacy `UnityEngine.Input` API. In Unity, open:

`Edit > Project Settings > Player > Other Settings > Configuration > Active Input Handling`

and select **Input System Package (New)**. Unity may request an editor restart.
