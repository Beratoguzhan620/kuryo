import { describe, expect, it } from "vitest";
import { MAX_QUEUE, PointQueue } from "./tracker";

const at = (seconds: number) => new Date(Date.UTC(2026, 10, 6, 9, 0, seconds)).toISOString();
const point = (seconds: number) => ({ latitude: 41.29, longitude: 36.33, at: at(seconds) });

describe("PointQueue", () => {
  it("keeps one point every few seconds", () => {
    const queue = new PointQueue();
    expect([0, 1, 2, 3, 4, 6, 7].map((s) => queue.add(point(s)))).toEqual([true, false, false, true, false, true, false]);
    expect(queue.take().map((p) => p.at)).toEqual([at(0), at(3), at(6)]);
    expect(queue.size).toBe(0);
  });

  it("keeps only the newest points when the phone was offline for long", () => {
    const queue = new PointQueue();
    for (let i = 0; i < MAX_QUEUE + 10; i++) queue.add(point(i * 3));
    const batch = queue.take();
    expect(batch).toHaveLength(MAX_QUEUE);
    expect(batch[0]!.at).toBe(at(30));
  });

  it("puts a failed batch back in front of newer points", () => {
    const queue = new PointQueue();
    queue.add(point(0));
    const failed = queue.take();
    queue.add(point(5));
    queue.restore(failed);
    expect(queue.take().map((p) => p.at)).toEqual([at(0), at(5)]);
  });

  it("sends where the courier stopped even when it came too soon after the last point", () => {
    const queue = new PointQueue();
    queue.add(point(0));
    queue.take();
    expect(queue.add({ ...point(2), latitude: 41.3 })).toBe(false);
    expect(queue.settle()?.latitude).toBe(41.3);
    expect(queue.take().map((p) => p.latitude)).toEqual([41.3]);
    expect(queue.settle()).toBeNull();
  });
});
