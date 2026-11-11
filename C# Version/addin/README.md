# Assembly Reader (Inventor add-in)

A button inside Inventor that opens a window with everything in the active assembly or part.

- **Tree (left):** components at every level, constraints, joints, patterns, unique files with
  quantity, representations, parameters (user, model, reference), iProperties, work features,
  iLogic rules and referenced files. For parts: features, sketches (lines, circles, arcs,
  dimensions) and bodies. Each component has a **File:** item that opens the part or
  sub-assembly behind it.
- **Properties (right):** every property the Inventor API has for the selected item, read from
  Inventor itself, so nothing is left out and new Inventor versions need no change.
- **All API objects** (grey, last item under each node): opens every object the API links to the
  item. Use it for anything the tree has no group for. **Inventor application** at the bottom
  does the same for Inventor as a whole.
- **Select in Inventor** (or double-click an item without children): selects the component, constraint or feature in the model.
- **Export XML...:** writes the selected item and everything below it, with all properties.
- **Copy properties**, or Ctrl+C in the list: copies the rows for Excel.
- **Find:** searches the opened part of the tree.

The reader only reads. It changes nothing in the model.

Raw API values are in Inventor's internal units: lengths in **cm**, angles in **radians**.
Positions, boxes and "Value in its unit" are converted and say mm.

## Build and install

1. Build, one of:
   - Inventor 2024 or older, no Visual Studio:
     `pwsh tools\build.ps1 -RefDir <.NET 4.8 reference assemblies> -NoApp -InventorDir "C:\Program Files\Autodesk\Inventor 2024"`
   - With the .NET SDK or Visual Studio:
     `dotnet build addin\InventorReader.csproj -c Release -p:InventorVersion=2024`
     Use the year of your Inventor. 2025 and newer build for .NET 8, older ones for .NET Framework 4.8.
2. Close Inventor.
3. `powershell -ExecutionPolicy Bypass -File tools\install-reader.ps1`
4. Start Inventor and open an assembly. The button **Assembly Reader** is on the **Tools** tab, panel **Sizer**.

If the button is missing: *Tools → Add-Ins*, select **Assembly Reader (Inventor Sizer)**, tick
*Loaded* and *Load Automatically*.

Remove it with `tools\install-reader.ps1 -Remove`.

## Code

| File | Contents |
|---|---|
| `AddInServer.cs` | The add-in class Inventor loads; adds the button. The only file that needs `Autodesk.Inventor.Interop.dll`. |
| `Api.cs` | Reads any API object through its COM type information: property names, values, enum names. |
| `Tree.cs` | The tree layout and the XML export. |
| `ReaderForm.cs` | The window. |
| `InventorSizer.Reader.addin` | Tells Inventor where the DLL is. |
| `tests\` | Tests for the tree and export on stand-in objects: `dotnet run` in that folder. |
