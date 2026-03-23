using Microsoft.AspNetCore.Mvc;
using Seed.Dashboard.Models;

namespace Seed.Dashboard.Controllers;

/// <summary>
/// REST API for simulation control and data retrieval.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class SimController : ControllerBase
{
    private readonly SimulationRunner _runner;
    
    public SimController(SimulationRunner runner)
    {
        _runner = runner;
    }
    
    /// <summary>
    /// Get current simulation status.
    /// </summary>
    [HttpGet("status")]
    public ActionResult<SimulationStatusDto> GetStatus()
    {
        return Ok(_runner.GetStatus());
    }
    
    /// <summary>
    /// Start/resume simulation.
    /// </summary>
    [HttpPost("play")]
    public ActionResult Play()
    {
        _runner.Play();
        return Ok(new { message = "Simulation resumed" });
    }
    
    /// <summary>
    /// Pause simulation.
    /// </summary>
    [HttpPost("pause")]
    public ActionResult Pause()
    {
        _runner.Pause();
        return Ok(new { message = "Simulation paused" });
    }
    
    /// <summary>
    /// Execute a single step.
    /// </summary>
    [HttpPost("step")]
    public ActionResult Step()
    {
        _runner.Step();
        return Ok(new { message = "Step requested" });
    }
    
    /// <summary>
    /// Set simulation speed.
    /// </summary>
    [HttpPost("speed")]
    public ActionResult SetSpeed([FromBody] SpeedRequest request)
    {
        _runner.SetSpeed(request.Speed);
        return Ok(new { message = $"Speed set to {request.Speed}x" });
    }
    
    /// <summary>
    /// Select an agent to view.
    /// </summary>
    [HttpPost("select-agent")]
    public ActionResult SelectAgent([FromBody] SelectAgentRequest request)
    {
        _runner.SelectAgent(request.Index);
        return Ok(new { message = $"Selected agent {request.Index}" });
    }
    
    /// <summary>
    /// Reset simulation.
    /// </summary>
    [HttpPost("reset")]
    public ActionResult Reset()
    {
        _runner.Reset();
        return Ok(new { message = "Simulation reset" });
    }
    
    /// <summary>
    /// Get current world frame.
    /// </summary>
    [HttpGet("frame")]
    public ActionResult<WorldFrameDto> GetFrame()
    {
        var frame = _runner.GetCurrentFrame();
        if (frame == null)
            return NotFound();
        return Ok(frame);
    }
    
    /// <summary>
    /// Get brain snapshot for selected agent.
    /// </summary>
    [HttpGet("brain")]
    public ActionResult<BrainSnapshotDto> GetBrain()
    {
        var brain = _runner.GetSelectedBrainSnapshot();
        if (brain == null)
            return NotFound();
        return Ok(brain);
    }
    
    /// <summary>
    /// Get generation history for charts.
    /// </summary>
    [HttpGet("history")]
    public ActionResult<IReadOnlyList<GenerationStatsDto>> GetHistory()
    {
        return Ok(_runner.GenerationHistory);
    }
    
    /// <summary>
    /// Start recording frames for replay.
    /// </summary>
    [HttpPost("start-recording")]
    public ActionResult StartRecording()
    {
        _runner.StartRecording();
        return Ok(new { message = "Recording started" });
    }
    
    /// <summary>
    /// Stop recording and get recorded frames.
    /// </summary>
    [HttpPost("stop-recording")]
    public ActionResult<List<WorldFrameDto>> StopRecording()
    {
        var frames = _runner.StopRecording();
        return Ok(frames);
    }
    
    /// <summary>
    /// Get current replay buffer.
    /// </summary>
    [HttpGet("replay")]
    public ActionResult<IReadOnlyList<WorldFrameDto>> GetReplay()
    {
        return Ok(_runner.GetReplayBuffer());
    }
    
    /// <summary>
    /// Check if currently recording.
    /// </summary>
    [HttpGet("is-recording")]
    public ActionResult<bool> IsRecording()
    {
        return Ok(_runner.IsRecording);
    }
}

public sealed record SpeedRequest(float Speed);
public sealed record SelectAgentRequest(int Index);

