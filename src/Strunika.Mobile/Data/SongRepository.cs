using SQLite;
using Strunika.Mobile.Models;

namespace Strunika.Mobile.Data;

public interface ISongRepository
{
    Task<List<Song>> GetAllAsync();
    /// <summary>The newest songs only — what the Songs tab shows first.</summary>
    Task<List<Song>> GetRecentAsync(int count);
    Task<Song?> GetAsync(int id);
    Task<int> InsertAsync(Song song);
    Task UpdateAsync(Song song);
    Task DeleteAsync(int id);
    Task<int> CountAsync();
}

/// <summary>
/// SQLite behind the library (sqlite-net-pcl). One file in the app data
/// directory; the schema is created on first use. Songs are few (hundreds
/// at most) so the list is always loaded whole and filtered in memory.
/// </summary>
public sealed class SongRepository : ISongRepository
{
    private readonly SQLiteAsyncConnection _db;
    private readonly Task _ready;

    public SongRepository() : this(Path.Combine(FileSystem.AppDataDirectory, "strunika.db3")) { }

    public SongRepository(string path)
    {
        _db = new SQLiteAsyncConnection(path,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        _ready = _db.CreateTableAsync<Song>();
    }

    public async Task<List<Song>> GetAllAsync()
    {
        await _ready;
        return await _db.Table<Song>().OrderByDescending(s => s.CreatedAt).ToListAsync();
    }

    public async Task<List<Song>> GetRecentAsync(int count)
    {
        await _ready;
        return await _db.Table<Song>().OrderByDescending(s => s.CreatedAt).Take(count).ToListAsync();
    }

    public async Task<Song?> GetAsync(int id)
    {
        await _ready;
        return await _db.FindAsync<Song>(id);
    }

    public async Task<int> InsertAsync(Song song)
    {
        await _ready;
        await _db.InsertAsync(song);
        return song.Id;
    }

    public async Task UpdateAsync(Song song)
    {
        await _ready;
        await _db.UpdateAsync(song);
    }

    public async Task DeleteAsync(int id)
    {
        await _ready;
        await _db.DeleteAsync<Song>(id);
    }

    public async Task<int> CountAsync()
    {
        await _ready;
        return await _db.Table<Song>().CountAsync();
    }
}
