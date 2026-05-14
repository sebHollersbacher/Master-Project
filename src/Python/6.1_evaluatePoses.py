import json
import sys
import argparse
import numpy as np
from pathlib import Path
from scipy.spatial import cKDTree
from itertools import combinations

MODEL_POINTS = {
    "pikachu": np.array([
        [-0.032902, 0.04224, 0.02885],  # left cheeck
        [0.039567, 0.044448, 0.029131],  # right cheeck
        [-0.071736, 0.107214, 0.022556],  # left ear
        [0.059494, 0.121871, 0.021735],  # right ear
        [-0.05225, -0.037227, 0.033301],  # left foot
        [0.031411, -0.106078, 0.03378],  # right foot
        [0.003817, 0.031145, 0.045193],  # mouth
        [-0.007097, -0.033314, -0.067379],  # Tail Start
        [0.046248, -0.010721, -0.020798],  # Brown top
        [0.009193, -0.084459, -0.031145]  # tail bottom
    ]),

    "racket": np.array([
        [0.001079, -0.142991, -0.01148],  # 0: label/sticker
        [0.066402, -0.031275, -0.00055],  # 1: head left (widest)
        [-0.075178, 0.01246, 0.000103],  # 2: head right (widest)
        [-0.001795, 0.105068, -0.001177],  # 3: head top center
        [0.001159, -0.052217, -0.006652],  # 4: junction center purple side
        [-0.012959, -0.054215, 0.002821],  # 5: junction right black side
        [-0.041474, -0.05266, -0.003546],  # 6: junction left purple side
        [-0.031743, -0.03634, -0.007068],  # 7: rubber bottom-right purple side
        [0.004968, -0.044034, 0.006658]  # 8: rubber bottom-left black side
    ]),

    "pen": np.array([
        [0.000000, 0.094079, 0.000000],  # 0: Tip
        [-0.002321, 0.08273, -0.002907],  # 1: corner wood 1
        [-0.00221, 0.082837, 0.002933],  # 2: corner wood 2
        [0.003344, 0.083185, -0.000018],  # 3: corner wood 3
        [0.000181, 0.013831, -0.003172],  # 5: gold middle
        [0.000268, -0.072056, -0.003084],  # 8: end 1
        [-0.00282, -0.072235, 0.001861],  # 9: end 2
        [0.003255, -0.072035, 0.001596],  # 10: end 3
        [0.000000, -0.088983, 0.000000]  # 11: rubber
    ]),
}

CAMERA_MATRIX = np.array([
    [433.08, 0.00, 318.235],
    [0.00, 433.08, 318.675],
    [0.00, 0.00, 1.000]
])


# ══════════════════════════════════════════════════════════════════
#  Metrics
# ══════════════════════════════════════════════════════════════════

def compute_add(pts, T_pred, T_gt):
    p = (T_pred[:3,:3] @ pts.T).T + T_pred[:3,3]
    g = (T_gt[:3,:3]   @ pts.T).T + T_gt[:3,3]
    return float(np.mean(np.linalg.norm(p - g, axis=1))) * 1000.0

def compute_adds(pts, T_pred, T_gt):
    p = (T_pred[:3,:3] @ pts.T).T + T_pred[:3,3]
    g = (T_gt[:3,:3]   @ pts.T).T + T_gt[:3,3]
    d, _ = cKDTree(g).query(p)
    return float(np.mean(d)) * 1000.0

def rotation_error_deg(R_pred, R_gt):
    trace = np.clip(np.trace(R_pred @ R_gt.T), -1.0, 3.0)
    return float(np.degrees(np.arccos(np.clip((trace - 1) / 2, -1, 1))))

def translation_error_mm(t_pred, t_gt):
    return float(np.linalg.norm(t_pred - t_gt)) * 1000.0

def reprojection_error_px(pts, T_pred, T_gt, K):
    def proj(T):
        c = (T[:3,:3] @ pts.T).T + T[:3,3]
        c = c[c[:,2] > 0]
        if len(c) == 0: return None
        p = (K @ c.T).T; return p[:,:2] / p[:,2:3]
    a, b = proj(T_pred), proj(T_gt)
    if a is None or b is None: return float('nan')
    n = min(len(a), len(b))
    return float(np.mean(np.linalg.norm(a[:n] - b[:n], axis=1)))


# ══════════════════════════════════════════════════════════════════
#  Helpers
# ══════════════════════════════════════════════════════════════════

def unity_colmajor_to_4x4(arr16):
    m = np.zeros((4,4))
    for c in range(4):
        for r in range(4):
            m[r,c] = arr16[c*4+r]
    return m

def compute_auc(errors, max_thresh, steps=100):
    th = np.linspace(0, max_thresh, steps)
    recalls = np.array([(errors <= t).mean() for t in th])
    _trapz = getattr(np, "trapezoid", None) or np.trapz
    return float(_trapz(recalls, th) / max_thresh)

def model_diameter(pts):
    return max(np.linalg.norm(pts[i]-pts[j]) for i,j in combinations(range(len(pts)),2))

def safe_get(d, *keys, default=0.0):
    for k in keys:
        if k in d: return d[k]
    return default


# ══════════════════════════════════════════════════════════════════
#  Per-mode analysis
# ══════════════════════════════════════════════════════════════════

class ModeResult:
    """Collects all metrics for one evaluation run."""
    def __init__(self, mode_name, eval_mode):
        self.name = mode_name
        self.eval_mode = eval_mode

        # Detection
        self.det_add = []; self.det_adds = []; self.det_trans = []; self.det_rot = []
        self.det_reproj = []; self.det_fids = []

        # Tracking
        self.trk_add = []; self.trk_adds = []; self.trk_trans = []; self.trk_rot = []
        self.trk_reproj = []; self.trk_fids = []

        # Timing
        self.preprocess = []; self.gpu_infer = []; self.readback = []
        self.postprocess = []; self.yolo_total = []; self.pnp = []
        self.tracking = []

        # Meta
        self.total_frames = 0
        self.warmup = 0
        self.det_interval = 0
        self.yolo_valid = 0
        self.pnp_valid = 0


def process_result_file(results_path, ground_truth, pts):
    """Process one _eval_*.json file and return a ModeResult."""
    with open(results_path) as f:
        results = json.load(f)

    mode = results.get("eval_mode", results.get("evalMode", "Unknown"))
    mr = ModeResult(results_path.stem, mode)
    mr.total_frames = results.get("total_frames", len(results["frames"]))
    mr.warmup = results.get("warmup_frames", results.get("warmupFrames", 0))
    mr.det_interval = results.get("detection_interval", results.get("detectionInterval", 0))
    mr.yolo_valid = results.get("yolo_valid_count", results.get("yoloValidCount", 0))
    mr.pnp_valid = results.get("pnp_valid_count", results.get("pnpValidCount", 0))

    frames = results["frames"]

    for i, fr in enumerate(frames):
        fid = fr["frame_id"]
        past_warmup = i >= mr.warmup

        # ── Detection pose ──
        if fr.get("detection_ran") and fr.get("pnp_valid") and fr.get("pnp_matrix"):
            if fid in ground_truth:
                T_gt = np.array(ground_truth[fid]["T_camera_object"])
                T_pred = unity_colmajor_to_4x4(fr["pnp_matrix"])
                mr.det_add.append(compute_add(pts, T_pred, T_gt))
                mr.det_adds.append(compute_adds(pts, T_pred, T_gt))
                mr.det_trans.append(translation_error_mm(T_pred[:3,3], T_gt[:3,3]))
                mr.det_rot.append(rotation_error_deg(T_pred[:3,:3], T_gt[:3,:3]))
                mr.det_reproj.append(reprojection_error_px(pts, T_pred, T_gt, CAMERA_MATRIX))
                mr.det_fids.append(fid)

            if past_warmup:
                mr.preprocess.append(safe_get(fr, "preprocess_ms"))
                mr.gpu_infer.append(safe_get(fr, "gpu_infer_ms"))
                mr.readback.append(safe_get(fr, "readback_ms"))
                mr.postprocess.append(safe_get(fr, "postprocess_ms"))
                mr.yolo_total.append(safe_get(fr, "yolo_total_ms", "yolo_time_ms"))
                mr.pnp.append(safe_get(fr, "pnp_time_ms"))

        # ── Tracking pose ──
        if fr.get("tracking_ran") and fr.get("tracking_matrix"):
            if fid in ground_truth:
                T_gt = np.array(ground_truth[fid]["T_camera_object"])
                T_trk = unity_colmajor_to_4x4(fr["tracking_matrix"])
                mr.trk_add.append(compute_add(pts, T_trk, T_gt))
                mr.trk_adds.append(compute_adds(pts, T_trk, T_gt))
                mr.trk_trans.append(translation_error_mm(T_trk[:3,3], T_gt[:3,3]))
                mr.trk_rot.append(rotation_error_deg(T_trk[:3,:3], T_gt[:3,:3]))
                mr.trk_reproj.append(reprojection_error_px(pts, T_trk, T_gt, CAMERA_MATRIX))
                mr.trk_fids.append(fid)

            if past_warmup:
                mr.tracking.append(safe_get(fr, "tracking_time_ms"))

    # Convert to arrays
    for attr in ['det_add','det_adds','det_trans','det_rot','det_reproj',
                 'trk_add','trk_adds','trk_trans','trk_rot','trk_reproj']:
        setattr(mr, attr, np.array(getattr(mr, attr)))

    return mr


def print_pose_block(label, add_e, adds_e, trans_e, rot_e, reproj_e, d10, diam_mm, n_total):
    """Print a full metric block."""
    n = len(add_e)
    print(f"\n  ── {label} ({n}/{n_total} frames) ──")
    if n == 0:
        print("    (no valid frames)")
        return

    print(f"    ADD   — mean: {add_e.mean():.2f}  median: {np.median(add_e):.2f}  std: {add_e.std():.2f} mm")
    print(f"    ADD-S — mean: {adds_e.mean():.2f}  median: {np.median(adds_e):.2f}  std: {adds_e.std():.2f} mm")

    auc_add  = compute_auc(add_e, 100.0)
    auc_adds = compute_auc(adds_e, 100.0)
    print(f"    AUC-ADD (≤100mm): {auc_add:.4f}   AUC-ADD-S (≤100mm): {auc_adds:.4f}")
    print(f"    Recall ADD  < 0.1d ({d10:.1f}mm): {(add_e < d10).mean()*100:.1f}%")
    print(f"    Recall ADDS < 0.1d ({d10:.1f}mm): {(adds_e < d10).mean()*100:.1f}%")

    for th in [5, 10, 20, 50]:
        print(f"    ADD/ADDS ≤{th:>2d}mm: {(add_e<=th).mean()*100:.1f}% / {(adds_e<=th).mean()*100:.1f}%")

    print(f"    Trans (mm) — mean: {trans_e.mean():.2f}  median: {np.median(trans_e):.2f}")
    print(f"    Rot   (°)  — mean: {rot_e.mean():.2f}  median: {np.median(rot_e):.2f}")

    for t, r in [(20,2), (50,5)]:
        print(f"    Recall {t}mm-{r}°: {((trans_e<=t)&(rot_e<=r)).mean()*100:.1f}%")

    vr = reproj_e[~np.isnan(reproj_e)] if reproj_e is not None and len(reproj_e) > 0 else np.array([])
    if len(vr) > 0:
        print(f"    Reproj (px) — mean: {vr.mean():.2f}  median: {np.median(vr):.2f}")


def print_timing_line(label, times):
    if not times: return
    t = np.array(times)
    print(f"    {label:<14s} — mean: {t.mean():>7.2f}ms  median: {np.median(t):>7.2f}ms  std: {t.std():>6.2f}ms")


def print_mode_report(mr, d10, diam_mm):
    """Print full report for one ModeResult."""
    print(f"\n{'='*70}")
    print(f"  {mr.eval_mode.upper()} — {mr.name}")
    print(f"{'='*70}")
    print(f"  Frames: {mr.total_frames}  |  Warmup: {mr.warmup}  |  Det interval: {mr.det_interval}")
    print(f"  YOLO valid: {mr.yolo_valid}  |  PnP valid: {mr.pnp_valid}")

    if len(mr.det_add) > 0:
        print_pose_block("Detection (YOLO+PnP)", mr.det_add, mr.det_adds,
                         mr.det_trans, mr.det_rot, mr.det_reproj, d10, diam_mm, mr.total_frames)

    if len(mr.trk_add) > 0:
        print_pose_block("Tracking (M3T)", mr.trk_add, mr.trk_adds,
                         mr.trk_trans, mr.trk_rot, mr.trk_reproj, d10, diam_mm, mr.total_frames)

    has_det_timing = len(mr.preprocess) > 0
    has_trk_timing = len(mr.tracking) > 0

    if has_det_timing or has_trk_timing:
        print(f"\n  ── Timing (excl. {mr.warmup} warmup) ──")
        if has_det_timing:
            print("    Detection:")
            print_timing_line("Preprocess",  mr.preprocess)
            print_timing_line("GPU Infer",   mr.gpu_infer)
            print_timing_line("Readback",    mr.readback)
            print_timing_line("Postprocess", mr.postprocess)
            print_timing_line("YOLO total",  mr.yolo_total)
            print_timing_line("PnP",         mr.pnp)
        if has_trk_timing:
            print("    Tracking:")
            print_timing_line("Per frame",   mr.tracking)


# ══════════════════════════════════════════════════════════════════
#  Plots
# ══════════════════════════════════════════════════════════════════

def generate_plots(mode_results, d10, diam_mm, out_dir, target):
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        print("\n  (matplotlib not installed — skipping plots)")
        return

    # Color map for modes
    colors = {
        "DetectionOnly": "#e15759",
        "TrackingOnly":  "#4e79a7",
        "FullPipeline":  "#59a14f",
    }

    # ── Figure 1: ADD curves per mode ──
    fig, axes = plt.subplots(1, 3, figsize=(18, 5))
    fig.suptitle(f"Pose Evaluation — {target.upper()}", fontsize=14)

    # ADD over frames
    ax = axes[0]
    for mr in mode_results:
        c = colors.get(mr.eval_mode, "gray")
        if len(mr.trk_add) > 0:
            ax.plot(mr.trk_add, lw=0.5, alpha=0.7, color=c,
                    label=f"{mr.eval_mode} tracking (mean={mr.trk_add.mean():.1f})")
        if len(mr.det_add) > 0 and mr.eval_mode == "DetectionOnly":
            ax.plot(mr.det_add, lw=0.5, alpha=0.7, color=c, ls="--",
                    label=f"Detection (mean={mr.det_add.mean():.1f})")
    ax.axhline(d10, color="green", ls=":", lw=1, label=f"0.1d={d10:.0f}mm")
    ax.set_xlabel("Frame"); ax.set_ylabel("ADD (mm)"); ax.set_title("ADD over Time")
    ax.legend(fontsize=7); ax.set_ylim(bottom=0)

    # ADD AUC curves
    ax = axes[1]
    th = np.linspace(0, 100, 300)
    for mr in mode_results:
        c = colors.get(mr.eval_mode, "gray")
        if len(mr.trk_add) > 0:
            auc = compute_auc(mr.trk_add, 100)
            ax.plot(th, [(mr.trk_add<=t).mean() for t in th], color=c,
                    label=f"{mr.eval_mode} trk (AUC={auc:.3f})")
        if len(mr.det_add) > 0:
            auc = compute_auc(mr.det_add, 100)
            ax.plot(th, [(mr.det_add<=t).mean() for t in th], color=c, ls="--",
                    label=f"{mr.eval_mode} det (AUC={auc:.3f})")
    ax.axvline(d10, color="green", ls=":", lw=1)
    ax.set_xlabel("Threshold (mm)"); ax.set_ylabel("Recall")
    ax.set_title("ADD AUC Curves"); ax.legend(fontsize=7); ax.grid(True, alpha=0.3)

    # Timing breakdown (use first mode that has detection timing)
    ax = axes[2]
    det_mr = next((mr for mr in mode_results if len(mr.preprocess) > 0), None)
    if det_mr:
        labels = ["Pre", "GPU", "Read", "Post", "PnP"]
        means = [np.mean(det_mr.preprocess), np.mean(det_mr.gpu_infer),
                 np.mean(det_mr.readback), np.mean(det_mr.postprocess),
                 np.mean(det_mr.pnp) if det_mr.pnp else 0]
        trk_mr = next((mr for mr in mode_results if len(mr.tracking) > 0), None)
        if trk_mr:
            labels.append("Track")
            means.append(np.mean(trk_mr.tracking))
        bar_colors = ["#4e79a7","#f28e2b","#e15759","#76b7b2","#59a14f","#edc948"][:len(labels)]
        bars = ax.bar(labels, means, color=bar_colors)
        ax.bar_label(bars, fmt="%.1f", fontsize=8)
        ax.set_ylabel("Time (ms)"); ax.set_title("Avg Stage Timing")
    else:
        trk_mr = next((mr for mr in mode_results if len(mr.tracking) > 0), None)
        if trk_mr:
            ax.hist(trk_mr.tracking, bins=40, alpha=0.7, color="#4e79a7")
            ax.set_xlabel("ms"); ax.set_ylabel("Count"); ax.set_title("Tracking Time Dist.")

    plt.tight_layout()
    fig.savefig(out_dir / "evaluation_plots.png", dpi=150)
    print(f"\n  Plots saved to {out_dir / 'evaluation_plots.png'}")
    plt.close(fig)

    # ── Figure 2: Rotation comparison ──
    if any(len(mr.trk_rot) > 0 for mr in mode_results):
        fig2, axes2 = plt.subplots(1, 2, figsize=(12, 5))
        fig2.suptitle(f"Rotation & Translation — {target.upper()}", fontsize=13)

        ax = axes2[0]
        for mr in mode_results:
            c = colors.get(mr.eval_mode, "gray")
            if len(mr.trk_rot) > 0:
                ax.plot(mr.trk_rot, lw=0.5, alpha=0.7, color=c, label=f"{mr.eval_mode} trk")
            if len(mr.det_rot) > 0 and mr.eval_mode == "DetectionOnly":
                ax.plot(mr.det_rot, lw=0.5, alpha=0.7, color=c, ls="--", label=f"Det only")
        ax.set_xlabel("Frame"); ax.set_ylabel("°"); ax.set_title("Rotation Error")
        ax.legend(fontsize=7)

        ax = axes2[1]
        for mr in mode_results:
            c = colors.get(mr.eval_mode, "gray")
            if len(mr.trk_trans) > 0:
                ax.plot(mr.trk_trans, lw=0.5, alpha=0.7, color=c, label=f"{mr.eval_mode} trk")
            if len(mr.det_trans) > 0 and mr.eval_mode == "DetectionOnly":
                ax.plot(mr.det_trans, lw=0.5, alpha=0.7, color=c, ls="--", label=f"Det only")
        ax.set_xlabel("Frame"); ax.set_ylabel("mm"); ax.set_title("Translation Error")
        ax.legend(fontsize=7)

        plt.tight_layout()
        fig2.savefig(out_dir / "evaluation_rot_trans.png", dpi=150)
        print(f"  Saved {out_dir / 'evaluation_rot_trans.png'}")
        plt.close(fig2)


# ══════════════════════════════════════════════════════════════════
#  CSV export
# ══════════════════════════════════════════════════════════════════

def export_csv(mode_results, out_dir):
    for mr in mode_results:
        csv_path = out_dir / f"eval_{mr.eval_mode.lower()}_detailed.csv"
        with open(csv_path, "w") as f:
            header = "frame_id"
            has_det = len(mr.det_add) > 0
            has_trk = len(mr.trk_add) > 0
            if has_det: header += ",det_add_mm,det_adds_mm,det_trans_mm,det_rot_deg"
            if has_trk: header += ",trk_add_mm,trk_adds_mm,trk_trans_mm,trk_rot_deg"
            f.write(header + "\n")

            det_i, trk_i = 0, 0
            all_fids = sorted(set(
                list(mr.det_fids) + list(mr.trk_fids)
            ))
            for fid in all_fids:
                line = fid
                if has_det:
                    if det_i < len(mr.det_fids) and mr.det_fids[det_i] == fid:
                        line += f",{mr.det_add[det_i]:.4f},{mr.det_adds[det_i]:.4f},{mr.det_trans[det_i]:.4f},{mr.det_rot[det_i]:.4f}"
                        det_i += 1
                    else:
                        line += ",,,,"
                if has_trk:
                    if trk_i < len(mr.trk_fids) and mr.trk_fids[trk_i] == fid:
                        line += f",{mr.trk_add[trk_i]:.4f},{mr.trk_adds[trk_i]:.4f},{mr.trk_trans[trk_i]:.4f},{mr.trk_rot[trk_i]:.4f}"
                        trk_i += 1
                    else:
                        line += ",,,,"
                f.write(line + "\n")

        print(f"  CSV: {csv_path}")


# ══════════════════════════════════════════════════════════════════
#  Main
# ══════════════════════════════════════════════════════════════════

def main(obj = "pen"):
    parser = argparse.ArgumentParser(description="Evaluate 6D pose results.")
    parser.add_argument("results", nargs="?", default=None,
                        help="Path to a single _eval_*.json OR a session folder containing them")
    parser.add_argument("ground_truth", nargs="?", default=None,
                        help="Path to _ground_truth.json")
    parser.add_argument("--target", default=None, help="pikachu, racket, or pen")
    args = parser.parse_args()

    # ── Resolve paths ──
    if args.results and args.ground_truth:
        results_input = Path(args.results)
        gt_path = Path(args.ground_truth)
    else:
        # Fallback — edit for your setup
        results_input = Path(f"validation_dataset/{obj}")
        gt_path = Path(f"validation_dataset/{obj}/_ground_truth.json")

    # ── Discover result files ──
    if results_input.is_dir():
        result_files = sorted(results_input.glob("_eval_*.json"))
        out_dir = results_input
    else:
        result_files = [results_input]
        out_dir = results_input.parent

    if not result_files:
        print(f"No _eval_*.json files found in {results_input}")
        sys.exit(1)

    # ── Load ground truth ──
    with open(gt_path) as f:
        ground_truth = json.load(f)

    # ── Resolve target ──
    with open(result_files[0]) as f:
        first_result = json.load(f)
    target = (args.target or first_result.get("target", obj)).lower()

    if target not in MODEL_POINTS:
        print(f"Unknown target '{target}'. Choose from: {list(MODEL_POINTS.keys())}")
        sys.exit(1)

    pts = MODEL_POINTS[target]
    diam = model_diameter(pts)
    diam_mm = diam * 1000.0
    d10 = 0.1 * diam_mm

    print(f"  Model: {target}  |  {len(pts)} keypoints  |  diameter: {diam_mm:.1f} mm")
    print(f"  Processing {len(result_files)} result file(s)...")

    # ── Process each result file ──
    mode_results = []
    for rp in result_files:
        mr = process_result_file(rp, ground_truth, pts)
        mode_results.append(mr)
        print_mode_report(mr, d10, diam_mm)

    # ── Comparison summary (if multiple modes) ──
    if len(mode_results) > 1:
        print(f"\n{'='*70}")
        print(f"  COMPARISON SUMMARY")
        print(f"{'='*70}")
        print(f"  {'Mode':<20s} {'ADD mean':>10s} {'ADD med':>10s} {'ADDS mean':>10s} {'<0.1d':>8s} {'Rot mean':>10s}")
        print(f"  {'-'*20} {'-'*10} {'-'*10} {'-'*10} {'-'*8} {'-'*10}")
        for mr in mode_results:
            # Pick the primary metric array (tracking if available, else detection)
            a = mr.trk_add if len(mr.trk_add) > 0 else mr.det_add
            s = mr.trk_adds if len(mr.trk_adds) > 0 else mr.det_adds
            r = mr.trk_rot if len(mr.trk_rot) > 0 else mr.det_rot
            if len(a) > 0:
                recall = (a < d10).mean() * 100
                print(f"  {mr.eval_mode:<20s} {a.mean():>10.2f} {np.median(a):>10.2f} {s.mean():>10.2f} {recall:>7.1f}% {r.mean():>10.2f}°")
            else:
                print(f"  {mr.eval_mode:<20s}  (no data)")

    print(f"\n{'='*70}")

    # ── Plots ──
    generate_plots(mode_results, d10, diam_mm, out_dir, target)

    # ── CSV ──
    export_csv(mode_results, out_dir)
    print()


if __name__ == "__main__":
    main()