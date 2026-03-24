using System.Collections.Generic;
using CTMemoryEditor.Models;

namespace CTMemoryEditor.Services;

public class EventFlagDefinitionService
{
    public List<EventVariable> GetKnownVariables()
    {
        return new List<EventVariable>
        {
            new EventVariable { ByteIndex = 0x51, Name = "Trial Innocent Votes", Description = "Number of jurors who voted not guilty" },
            new EventVariable { ByteIndex = 0x52, Name = "Silver Points", Description = "Currency used at the Millennial Fair" },
            new EventVariable { ByteIndex = 0x53, Name = "Kittens", Description = "Number of cats won at the tent" },
            new EventVariable { ByteIndex = 0x5F, Name = "Cat Food", Description = "Amount of cat food held" },
        };
    }

    public List<EventBitFlag> GetKnownFlags()
    {
        return new List<EventBitFlag>
        {
            new EventBitFlag { ByteIndex = 0x50, BitMask = 0x01, Name = "Marle bumped into Crono" },
            new EventBitFlag { ByteIndex = 0x50, BitMask = 0x02, Name = "Tempted by Marle's fortune" },
            new EventBitFlag { ByteIndex = 0x50, BitMask = 0x04, Name = "Just a bit tempted by Marle's fortune" },
            new EventBitFlag { ByteIndex = 0x50, BitMask = 0x08, Name = "Found out the Chancellor is framing King Guardia" },
            new EventBitFlag { ByteIndex = 0x54, BitMask = 0x01, Name = "Brought the girl her cat back" },
            new EventBitFlag { ByteIndex = 0x54, BitMask = 0x02, Name = "Talked to the girl who lost her cat before bringing it back to her" },
            new EventBitFlag { ByteIndex = 0x54, BitMask = 0x04, Name = "Ate the old man's lunch" },
            new EventBitFlag { ByteIndex = 0x55, BitMask = 0x01, Name = "Tried to sell Marle's pendant" },
            new EventBitFlag { ByteIndex = 0x55, BitMask = 0x02, Name = "Picked up pendant before talking to Marle" },
            new EventBitFlag { ByteIndex = 0x55, BitMask = 0x04, Name = "Talked to Marle about missing pendant" },
            new EventBitFlag { ByteIndex = 0x55, BitMask = 0x20, Name = "Marle yells about being kidnapped" },
            new EventBitFlag { ByteIndex = 0x55, BitMask = 0x40, Name = "Given Marle her pendant" },
            new EventBitFlag { ByteIndex = 0x5A, BitMask = 0x20, Name = "Lucca tries to change the past" },
            new EventBitFlag { ByteIndex = 0x5D, BitMask = 0x01, Name = "Obtained Crono Poyozo" },
            new EventBitFlag { ByteIndex = 0x5D, BitMask = 0x02, Name = "Obtained Marle Poyozo" },
            new EventBitFlag { ByteIndex = 0x5D, BitMask = 0x04, Name = "Obtained Lucca Poyozo" },
            new EventBitFlag { ByteIndex = 0x5D, BitMask = 0x08, Name = "Obtained Robo Poyozo" },
            new EventBitFlag { ByteIndex = 0x5D, BitMask = 0x10, Name = "Obtained Frog Poyozo" },
            new EventBitFlag { ByteIndex = 0x5D, BitMask = 0x20, Name = "Obtained Ayla Poyozo" },
            new EventBitFlag { ByteIndex = 0x5D, BitMask = 0x40, Name = "Obtained Magus Poyozo" },
            new EventBitFlag { ByteIndex = 0x5D, BitMask = 0x80, Name = "Crono won cat at fair" },
            new EventBitFlag { ByteIndex = 0x5E, BitMask = 0x01, Name = "Obtained Crono Clone" },
            new EventBitFlag { ByteIndex = 0x5E, BitMask = 0x02, Name = "Obtained Marle Clone" },
            new EventBitFlag { ByteIndex = 0x5E, BitMask = 0x04, Name = "Obtained Lucca Clone" },
            new EventBitFlag { ByteIndex = 0x5E, BitMask = 0x08, Name = "Obtained Robo Clone" },
            new EventBitFlag { ByteIndex = 0x5E, BitMask = 0x10, Name = "Obtained Frog Clone" },
            new EventBitFlag { ByteIndex = 0x5E, BitMask = 0x20, Name = "Obtained Ayla Clone" },
            new EventBitFlag { ByteIndex = 0x5E, BitMask = 0x40, Name = "Obtained Magus Clone" },
            new EventBitFlag { ByteIndex = 0x6D, BitMask = 0x80, Name = "Epoch trashed" },
            new EventBitFlag { ByteIndex = 0x7C, BitMask = 0x40, Name = "Lara loses her legs" },
            new EventBitFlag { ByteIndex = 0xE1, BitMask = 0x02, Name = "Met Spekkio" },
            new EventBitFlag { ByteIndex = 0xE1, BitMask = 0x04, Name = "Beat Spekkio E0" },
            new EventBitFlag { ByteIndex = 0xE1, BitMask = 0x08, Name = "Beat Spekkio E1" },
            new EventBitFlag { ByteIndex = 0xE1, BitMask = 0x10, Name = "Beat Spekkio E2" },
            new EventBitFlag { ByteIndex = 0xE1, BitMask = 0x20, Name = "Beat Spekkio E3" },
            new EventBitFlag { ByteIndex = 0xE1, BitMask = 0x40, Name = "Beat Spekkio E4" },
            new EventBitFlag { ByteIndex = 0xE1, BitMask = 0x80, Name = "Beat Spekkio E5" },
            new EventBitFlag { ByteIndex = 0xE2, BitMask = 0x01, Name = "Spekkio has met Robo" },
            new EventBitFlag { ByteIndex = 0xE2, BitMask = 0x02, Name = "Spekkio has met Ayla" },
            new EventBitFlag { ByteIndex = 0xE2, BitMask = 0x04, Name = "Spekkio has met Magus" },
            new EventBitFlag { ByteIndex = 0xF7, BitMask = 0x02, Name = "Told sapling woman to plant the tree" },
            new EventBitFlag { ByteIndex = 0xFE, BitMask = 0x02, Name = "Naga-ette Bromide obtained" },
            new EventBitFlag { ByteIndex = 0x13A, BitMask = 0x01, Name = "Fought Son of Sun" },
            new EventBitFlag { ByteIndex = 0x140, BitMask = 0x01, Name = "Mom mentioned Lucca dropping by" },
            new EventBitFlag { ByteIndex = 0x140, BitMask = 0x02, Name = "Mom gave Crono allowance" },
            new EventBitFlag { ByteIndex = 0x140, BitMask = 0x04, Name = "Mom has met Marle" },
            new EventBitFlag { ByteIndex = 0x140, BitMask = 0x08, Name = "Mom has met Lucca" },
            new EventBitFlag { ByteIndex = 0x140, BitMask = 0x10, Name = "Mom has met Robo" },
            new EventBitFlag { ByteIndex = 0x140, BitMask = 0x20, Name = "Mom has met Frog" },
            new EventBitFlag { ByteIndex = 0x140, BitMask = 0x40, Name = "Mom has met Ayla" },
            new EventBitFlag { ByteIndex = 0x140, BitMask = 0x80, Name = "Mom has met Magus" },
            new EventBitFlag { ByteIndex = 0x14A, BitMask = 0x08, Name = "Power Tab in long Geno Dome hallway" },
            new EventBitFlag { ByteIndex = 0x14E, BitMask = 0x08, Name = "Mother speaks to PCs in long Geno Dome hallway" },
            new EventBitFlag { ByteIndex = 0x190, BitMask = 0x01, Name = "Brought to Prison" },
            new EventBitFlag { ByteIndex = 0x1A2, BitMask = 0x10, Name = "Fought Medina Merchant" },
            new EventBitFlag { ByteIndex = 0x1E0, BitMask = 0x01, Name = "Spekkio has given Crono magic" },
            new EventBitFlag { ByteIndex = 0x1E0, BitMask = 0x02, Name = "Spekkio has given Marle magic" },
            new EventBitFlag { ByteIndex = 0x1E0, BitMask = 0x04, Name = "Spekkio has given Lucca magic" },
            new EventBitFlag { ByteIndex = 0x1E0, BitMask = 0x08, Name = "Spekkio has given Robo magic" },
            new EventBitFlag { ByteIndex = 0x1E0, BitMask = 0x10, Name = "Spekkio has given Frog magic" },
            new EventBitFlag { ByteIndex = 0x1E0, BitMask = 0x20, Name = "Spekkio has given Ayla magic" },
            new EventBitFlag { ByteIndex = 0x1E0, BitMask = 0x40, Name = "Spekkio has given Magus magic" }
        };
    }
}
