using Microsoft.AspNetCore.SignalR;

namespace NCS.CBT.Hubs;

public class ExamHub : Hub
{
    public async Task JoinViewerRoom()
    {
        // All proctors and admins see all students
        await Groups.AddToGroupAsync(Context.ConnectionId, "viewers");
    }

    public async Task LeaveViewerRoom()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "viewers");
    }

    // Students join their own group so the proctor can push a disqualify signal to them
    public async Task JoinStudentRoom(string studentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"student-{studentId}");
    }
}
