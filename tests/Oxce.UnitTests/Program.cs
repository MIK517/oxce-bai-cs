using Oxce.Rendering;

var surface = new IndexedSurface(2, 2);
surface.GetRow(1)[1] = 42;
if (surface.Pixels[3] != 42)
{
    throw new InvalidOperationException("IndexedSurface row addressing failed.");
}

Console.WriteLine("Smoke checks passed. Replace this runner with the selected test framework in Phase 0.");
