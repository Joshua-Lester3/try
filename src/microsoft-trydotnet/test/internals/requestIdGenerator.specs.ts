// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as chai from "chai";
import { buildSimpleIFrameDom } from "../domUtilities";
import * as trydotnet from "../../src/index";
import { IFrameMessageBus } from "../../src/internals/messageBus";
import { configureEmbeddableEditorIFrame } from "../../src/htmlDomHelpers";
import { RequestIdGenerator } from "../../src/internals/requestIdGenerator";

chai.should();

describe("a request id generator", () => {

    const defaultConfiguration: trydotnet.Configuration = { hostOrigin: "https://learn.microsoft.com" };

    it("uses local generator ", async () => {
        var dom = buildSimpleIFrameDom(defaultConfiguration);

        let iframe = (dom.window.document.querySelector("iframe")) as HTMLIFrameElement;
        iframe = configureEmbeddableEditorIFrame(iframe, defaultConfiguration);

        new IFrameMessageBus(iframe, dom.window as unknown as Window);

        let generator = new RequestIdGenerator();

        let opId = await generator.getNewRequestId();

        opId.should.contain("trydotnetjs.session");
    });
});