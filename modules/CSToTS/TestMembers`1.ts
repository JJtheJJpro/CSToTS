import Oh from '../Oh'

export namespace CSToTS {
    export class TestMembers<T> extends Oh {
        public constructor() {
            super();
            throw new Error("not yet implemented");
        }

    }

    export namespace TestMembers {
        export class Test {
            public constructor() {
                throw new Error("not yet implemented");
            }

        }
    }
}

export default CSToTS;
