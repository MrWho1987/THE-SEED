using Microsoft.AspNetCore.SignalR;
using Seed.Dashboard.Models;

namespace Seed.Dashboard.Hubs;

/// <summary>
/// SignalR hub for real-time simulation updates.
/// </summary>
public sealed class SimulationHub : Hub
{
    private readonly SimulationRunner _runner;
    private readonly ILogger<SimulationHub> _logger;
    
    public SimulationHub(SimulationRunner runner, ILogger<SimulationHub> logger)
    {
        _runner = runner;
        _logger = logger;
    }
    
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {Id}", Context.ConnectionId);
        
        // Send current status immediately
        await Clients.Caller.SendAsync("Status", _runner.GetStatus());
        
        // Send current frame if available
        var frame = _runner.GetCurrentFrame();
        if (frame != null)
        {
            await Clients.Caller.SendAsync("WorldFrame", frame);
        }
        
        // Send generation history
        await Clients.Caller.SendAsync("GenerationHistory", _runner.GenerationHistory);

        var overrides = _runner.GetWorldOverrides();
        if (overrides != null)
            await Clients.Caller.SendAsync("WorldOverrides", overrides);

        await base.OnConnectedAsync();
    }
    
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {Id}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
    
    /// <summary>
    /// Client requests to play/resume simulation.
    /// </summary>
    public void Play()
    {
        _runner.Play();
    }
    
    /// <summary>
    /// Client requests to pause simulation.
    /// </summary>
    public void Pause()
    {
        _runner.Pause();
    }
    
    /// <summary>
    /// Client requests a single step.
    /// </summary>
    public void Step()
    {
        _runner.Step();
    }
    
    /// <summary>
    /// Client sets simulation speed.
    /// </summary>
    public void SetSpeed(float speed)
    {
        _runner.SetSpeed(speed);
    }
    
    /// <summary>
    /// Client selects an agent to track.
    /// </summary>
    public void SelectAgent(int index)
    {
        _runner.SelectAgent(index);
    }
    
    /// <summary>
    /// Client requests simulation reset.
    /// </summary>
    public void Reset()
    {
        _runner.Reset();
    }
    
    /// <summary>
    /// Client requests current status.
    /// </summary>
    public async Task GetStatus()
    {
        await Clients.Caller.SendAsync("Status", _runner.GetStatus());
    }
    
    /// <summary>
    /// Client requests current frame.
    /// </summary>
    public async Task GetFrame()
    {
        var frame = _runner.GetCurrentFrame();
        if (frame != null)
        {
            await Clients.Caller.SendAsync("WorldFrame", frame);
        }
    }
    
    /// <summary>
    /// Client requests brain snapshot for selected agent.
    /// </summary>
    public async Task GetBrainSnapshot()
    {
        var snapshot = _runner.GetSelectedBrainSnapshot();
        if (snapshot != null)
        {
            await Clients.Caller.SendAsync("BrainSnapshot", snapshot);
        }
    }

    public void ApplyWorldOverride(WorldOverrideDto dto)
    {
        _runner.ApplyWorldOverride(dto);
    }

    public void ClearWorldOverride()
    {
        _runner.ClearWorldOverride();
    }
}


