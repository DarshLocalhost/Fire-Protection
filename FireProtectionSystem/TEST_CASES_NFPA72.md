# Test Cases: NFPA 72 Audio/Visual Notification Appliance Coverage Solver

This document outlines the test cases covering the new **NFPA 72 Audio/Visual Notification Appliance Coverage Solver** (`Nfpa72AudibleVisualSolver.cs`), its integration in `Nfpa72NotificationApplianceRules.cs`, and corresponding automated unit test suites.

---

## 1. Unit Test Suite (Automated via `NotificationApplianceRulesTests.cs`)

Run command:
```powershell
dotnet run --project FireProtection.Tests/FireProtection.Tests.csproj -c Revit2025
```

### Test Group 1: Visible Notification (NFPA 72 Table 18.5.5.4.1(a) Candela Lookups)

| Test ID | Case Description | Input Dimension / Rating | Expected Output | Verification Standard |
| :--- | :--- | :--- | :--- | :--- |
| **TC-VIS-01** | Small Room Strobe Rating | Max Dimension = $15.0\text{ ft}$ | **15 cd** | NFPA 72 Table 18.5.5.4.1(a) |
| **TC-VIS-02** | 20 ft Room Strobe Rating | Max Dimension = $20.0\text{ ft}$ | **15 cd** | NFPA 72 Table 18.5.5.4.1(a) |
| **TC-VIS-03** | 25 ft Room Strobe Rating | Max Dimension = $25.0\text{ ft}$ | **30 cd** | NFPA 72 Table 18.5.5.4.1(a) |
| **TC-VIS-04** | 30 ft Room Strobe Rating | Max Dimension = $30.0\text{ ft}$ | **30 cd** | NFPA 72 Table 18.5.5.4.1(a) |
| **TC-VIS-05** | 35 ft Room Strobe Rating | Max Dimension = $35.0\text{ ft}$ | **60 cd** | NFPA 72 Table 18.5.5.4.1(a) |
| **TC-VIS-06** | 45 ft Room Strobe Rating | Max Dimension = $45.0\text{ ft}$ | **95 cd** | NFPA 72 Table 18.5.5.4.1(a) |
| **TC-VIS-07** | 55 ft Room Strobe Rating | Max Dimension = $55.0\text{ ft}$ | **135 cd** | NFPA 72 Table 18.5.5.4.1(a) |
| **TC-VIS-08** | 65 ft Room Strobe Rating | Max Dimension = $65.0\text{ ft}$ | **185 cd** | NFPA 72 Table 18.5.5.4.1(a) |
| **TC-VIS-09** | 15 cd Coverage Reach | Rating = $15\text{ cd}$ | Max Dimension = **$20.0\text{ ft}$** | Inverted Table Lookup |
| **TC-VIS-10** | 30 cd Coverage Reach | Rating = $30\text{ cd}$ | Max Dimension = **$30.0\text{ ft}$** | Inverted Table Lookup |
| **TC-VIS-11** | 60 cd Coverage Reach | Rating = $60\text{ cd}$ | Max Dimension = **$40.0\text{ ft}$** | Inverted Table Lookup |
| **TC-VIS-12** | 95 cd Coverage Reach | Rating = $95\text{ cd}$ | Max Dimension = **$50.0\text{ ft}$** | Inverted Table Lookup |

---

### Test Group 2: Audible Sound Attenuation & Inverse Square Law (NFPA 72 §18.4)

$$L_p(d) = L_p(d_0) - 20 \log_{10}\left(\frac{d}{d_0}\right)$$

| Test ID | Case Description | Input Parameters | Expected Value | Verification Standard |
| :--- | :--- | :--- | :--- | :--- |
| **TC-AUD-01** | Reference Distance Level | $L_{\text{rated}} = 85.0\text{ dBA}$, $d = 10.0\text{ ft}$ | **$85.0\text{ dBA}$** | UL 464 reference distance |
| **TC-AUD-02** | First Distance Doubling (20 ft) | $L_{\text{rated}} = 85.0\text{ dBA}$, $d = 20.0\text{ ft}$ | **$78.98\text{ dBA}$** ($-6.02\text{ dBA}$) | Free-field spherical propagation |
| **TC-AUD-03** | Second Distance Doubling (40 ft)| $L_{\text{rated}} = 85.0\text{ dBA}$, $d = 40.0\text{ ft}$ | **$72.96\text{ dBA}$** ($-12.04\text{ dBA}$) | Free-field spherical propagation |
| **TC-AUD-04** | Maximum Throw to 70 dBA Target | $L_{\text{rated}} = 85.0\text{ dBA}$, $L_{\text{target}} = 70.0\text{ dBA}$ | **$56.23\text{ ft}$** | $10 \times 10^{(15/20)}$ |
| **TC-AUD-05** | Sound Level at Max Throw | $L_{\text{rated}} = 85.0\text{ dBA}$, $d = 56.23\text{ ft}$ | **$70.00\text{ dBA}$** | Exact conservation of target |
| **TC-AUD-06** | Underrated Appliance Check | $L_{\text{rated}} = 65.0\text{ dBA}$, $L_{\text{target}} = 70.0\text{ dBA}$ | Throw = **$0.0\text{ ft}$** | Appliance fails target at $10\text{ ft}$ |

---

### Test Group 3: Corridor Classification & Strobe Spacing (NFPA 72 §18.5.5.5)

| Test ID | Case Description | Dimensions ($W \times L$) | Expected Classification / Spacing | NFPA 72 Requirement |
| :--- | :--- | :--- | :--- | :--- |
| **TC-COR-01** | Narrow Corridor ($10 \times 50\text{ ft}$) | $10\text{ ft} \times 50\text{ ft}$ | **Corridor = True** | $W \le 20\text{ ft}$, $L/W = 5.0 \ge 2.5$ |
| **TC-COR-02** | Medium Corridor ($12 \times 35\text{ ft}$) | $12\text{ ft} \times 35\text{ ft}$ | **Corridor = True** | $W \le 20\text{ ft}$, $L/W = 2.92 \ge 2.5$ |
| **TC-COR-03** | Wide Space Exceeding 20 ft ($25 \times 100\text{ ft}$) | $25\text{ ft} \times 100\text{ ft}$ | **Corridor = False** (Treat as Room) | Width $> 20\text{ ft}$ (§18.5.5.5) |
| **TC-COR-04** | Squat Room ($20 \times 30\text{ ft}$) | $20\text{ ft} \times 30\text{ ft}$ | **Corridor = False** | Aspect ratio $1.5 < 2.5$ |
| **TC-COR-05** | Corridor Strobe Max Spacing | Standard Corridor | **$100.0\text{ ft}$** | NFPA 72 §18.5.5.5 |
| **TC-COR-06** | Corridor Strobe End Offset | Corridor Termination | **$\le 15.0\text{ ft}$** | NFPA 72 §18.5.5.5 |

---

### Test Group 4: Combined Horn/Strobe Strictest Constraint Governing

| Test ID | Case Description | Appliance Rating | Governing Constraint | Applied Max Spacing |
| :--- | :--- | :--- | :--- | :--- |
| **TC-COMB-01**| Low Candela, Moderate Horn | $15\text{ cd}$, $87\text{ dBA}$ | Stricter Audible / Visible limit | **$25.0\text{ ft}$** |
| **TC-COMB-02**| Wall Mount Sloped Reduction | $15\text{ cd}$, $87\text{ dBA}$, Wall, Sloped | Wall ($0.85$) + Sloped ($0.80$) | **$< 20.0\text{ ft}$** |

---

## 2. Interactive Testing via C# Interactive / Scratch Script

If you wish to test custom room dimensions or horn ratings directly, you can run a one-line evaluation script:

```powershell
node -e "
const ratedDba = 85;
const dist = 30;
const dbaAtDist = ratedDba - 20 * Math.log10(dist / 10);
console.log('dBA at ' + dist + ' ft: ' + dbaAtDist.toFixed(2) + ' dBA');
const maxThrow = 10 * Math.pow(10, (ratedDba - 70) / 20);
console.log('Max throw for 70 dBA: ' + maxThrow.toFixed(2) + ' ft');
"
```
Output:
```text
dBA at 30 ft: 75.46 dBA
Max throw for 70 dBA: 56.23 ft
```
