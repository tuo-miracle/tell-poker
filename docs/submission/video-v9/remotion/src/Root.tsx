import {Composition} from "remotion";
import {Film} from "./Film";
import edl from "./edl.json";
const total = (edl as {frames: number}[]).reduce((a, s) => a + s.frames, 0);
export const Root = () => <Composition id="Film" component={Film} durationInFrames={total} fps={30} width={1920} height={1080}/>;
