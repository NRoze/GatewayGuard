using System;
using System.Collections.Generic;
using System.Text;

namespace GatewayGuard.Enums;

public enum IdempotencyState
{
    InProgress = 1,
    Completed = 2,
    Failed = 3
}
