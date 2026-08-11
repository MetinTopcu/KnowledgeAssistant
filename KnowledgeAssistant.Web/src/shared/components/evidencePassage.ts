/**
 * The minimum a passage must carry to be rendered in the evidence table.
 *
 * **This is a view contract, not a shared domain type.** The retrieval and
 * agent responses have citation records with identical fields and different
 * meanings, and the server keeps them as separate types on exactly that
 * ground: in retrieval, `referenceNumber` is a position in a prompt the handler
 * built and is known before the model runs; in the agent, it is the order in
 * which the agent chose to look things up, which nobody scripted and which
 * collapses duplicates. Merging those would force one meaning onto the other
 * the first time either changed.
 *
 * So the *types* stay apart — `AnswerCitation` and `AgentCitation` are declared
 * in their own features — and only the table's rendering is shared. What the
 * numbers mean is supplied by each caller as column copy.
 */
export interface EvidencePassage {
  readonly referenceNumber: number;
  readonly chunkId: string;
  readonly documentId: string;
  readonly chunkOrder: number;
  readonly text: string;
  readonly score: number;
}
