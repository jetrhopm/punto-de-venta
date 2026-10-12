using System.Text.Json;

namespace JetVenta.Mobile;

public sealed class MobileShoppingListStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath = Path.Combine(FileSystem.AppDataDirectory, "shopping-list.json");

    public async Task<List<MobileShoppingListItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath)) return [];
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<MobileShoppingListItem>>(stream, JsonOptions, cancellationToken) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("No se pudo leer la lista de compras guardada en este teléfono.", exception);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(IReadOnlyCollection<MobileShoppingListItem> items, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var temporaryPath = _filePath + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken);
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            _gate.Release();
        }
    }
}

public sealed class MobileShoppingListItem : System.ComponentModel.INotifyPropertyChanged
{
    private decimal _quantity;
    private bool _isObtained;

    public Guid ProductId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UnitOfMeasure { get; set; } = string.Empty;
    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity == value) return;
            _quantity = value;
            PropertyChanged?.Invoke(this, new(nameof(Quantity)));
            PropertyChanged?.Invoke(this, new(nameof(QuantityDisplay)));
        }
    }
    public bool IsObtained
    {
        get => _isObtained;
        set
        {
            if (_isObtained == value) return;
            _isObtained = value;
            PropertyChanged?.Invoke(this, new(nameof(IsObtained)));
            PropertyChanged?.Invoke(this, new(nameof(StatusText)));
        }
    }
    public string QuantityDisplay => $"{Quantity:0.###} {UnitOfMeasure}".Trim();
    public string StatusText => IsObtained ? "Surtido" : "Por surtir";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
